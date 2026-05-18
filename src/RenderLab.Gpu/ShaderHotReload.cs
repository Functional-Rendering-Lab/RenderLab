using System.Collections.Concurrent;
using System.Diagnostics;

namespace RenderLab.Gpu;

/// <summary>
/// Debug-only shader hot reload. Watches the GLSL source tree (when
/// reachable from the running binary), shells out to <c>glslc</c> on
/// change, and asks the active <see cref="IPipeline"/> to rebuild its
/// VkPipelines from the freshly-emitted SPIR-V. Compile failures keep
/// the previous VkPipelines alive; only successful compiles trigger a
/// rebuild.
///
/// Owns a <see cref="FileSystemWatcher"/>. Threading: watcher events
/// enqueue paths onto a concurrent queue; <see cref="Pump"/> drains
/// them on the main loop thread so Vulkan calls stay single-threaded.
/// </summary>
public sealed class ShaderHotReload : IDisposable
{
    /// <summary>Source-tree directory containing <c>*.vert</c> / <c>*.frag</c>,
    /// or <c>null</c> when the tree could not be located (e.g. published
    /// build with no neighbouring repo).</summary>
    public string? SourceDir { get; }

    /// <summary>The runtime <c>shaders/</c> directory pipelines read at
    /// startup. Compiled <c>.spv</c> files are written here so the
    /// pipeline's existing load path picks up the new bytes.</summary>
    public string RuntimeDir { get; }

    /// <summary>True iff the watcher was constructed and <c>glslc</c> is
    /// on PATH. False disables all hot-reload work.</summary>
    public bool IsEnabled { get; }

    /// <summary>Application sets this to a callback that rebuilds the
    /// active pipeline's VkPipelines. Cleared on project teardown so a
    /// stale pipeline reference is never invoked.</summary>
    public Action<GpuState>? OnReload { get; set; }

    readonly Action<string> log;
    readonly string? glslc;
    readonly FileSystemWatcher? watcher;
    readonly ConcurrentQueue<string> dirty = new();
    DateTime lastEnqueue = DateTime.MinValue;
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(120);

    public ShaderHotReload(Action<string> log)
    {
        this.log = log;
        RuntimeDir = Path.Combine(AppContext.BaseDirectory, "shaders");
        SourceDir = LocateSourceDir();
        glslc = LocateGlslc();

        if (SourceDir is null)
        {
            log("hot reload disabled: shader source tree not found (release build?)");
            return;
        }
        if (glslc is null)
        {
            log("hot reload disabled: glslc not found on PATH");
            return;
        }

        watcher = new FileSystemWatcher(SourceDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        watcher.Filters.Add("*.vert");
        watcher.Filters.Add("*.frag");
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += (_, e) => Enqueue(e.FullPath);
        watcher.EnableRaisingEvents = true;
        IsEnabled = true;
        log($"hot reload watching {SourceDir}");
    }

    void OnChanged(object _, FileSystemEventArgs e) => Enqueue(e.FullPath);

    void Enqueue(string path)
    {
        if (!IsShaderSource(path)) return;
        dirty.Enqueue(path);
        lastEnqueue = DateTime.UtcNow;
    }

    static bool IsShaderSource(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".vert", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".frag", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Queue every shader in the source tree for recompile +
    /// reload, regardless of mtime. Wired to F5.</summary>
    public void RequestForceReload()
    {
        if (!IsEnabled || SourceDir is null) return;
        foreach (var pat in new[] { "*.vert", "*.frag" })
            foreach (var file in Directory.EnumerateFiles(SourceDir, pat, SearchOption.AllDirectories))
                dirty.Enqueue(file);
        lastEnqueue = DateTime.UtcNow;
    }

    /// <summary>Drain pending changes once the debounce window has
    /// elapsed. Called once per frame from <c>Application.Loop</c>.</summary>
    public void Pump(GpuState gpu)
    {
        if (!IsEnabled || dirty.IsEmpty || OnReload is null) return;
        if (DateTime.UtcNow - lastEnqueue < Debounce) return;

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (dirty.TryDequeue(out var p)) paths.Add(p);
        if (paths.Count == 0) return;

        var failures = new List<string>();
        var compiled = new List<string>();
        foreach (var src in paths)
        {
            if (!File.Exists(src)) continue;
            var outFile = Path.Combine(RuntimeDir, Path.GetFileName(src) + ".spv");
            if (TryCompile(src, outFile, out var err))
                compiled.Add(Path.GetFileName(src));
            else
                failures.Add($"{Path.GetFileName(src)}: {err}");
        }

        if (failures.Count > 0)
        {
            foreach (var f in failures) log("shader compile failed — " + f);
            return;
        }
        if (compiled.Count == 0) return;

        unsafe { gpu.Vk.DeviceWaitIdle(gpu.Device); }
        try
        {
            OnReload(gpu);
            log($"reloaded {compiled.Count} shader(s): {string.Join(", ", compiled)}");
        }
        catch (Exception ex)
        {
            log($"pipeline reload threw — {ex.Message}");
        }
    }

    bool TryCompile(string source, string output, out string error)
    {
        error = "";
        try
        {
            var psi = new ProcessStartInfo(glslc!, $"\"{source}\" -o \"{output}\"")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                error = stderr.Trim().Replace("\r\n", " | ").Replace('\n', '|');
                if (string.IsNullOrEmpty(error)) error = $"glslc exit code {p.ExitCode}";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static string? LocateSourceDir()
    {
        // Walk up from the executable until we find `src/RenderLab.Shaders/`.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "RenderLab.Shaders");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    static string? LocateGlslc()
    {
        var name = OperatingSystem.IsWindows() ? "glslc.exe" : "glslc";
        foreach (var path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                var candidate = Path.Combine(path, name);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry */ }
        }
        return null;
    }

    public void Dispose()
    {
        watcher?.Dispose();
    }
}
