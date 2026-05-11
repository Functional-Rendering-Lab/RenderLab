using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenderLab.Project;

/// <summary>
/// Reads and writes <see cref="EditorSettings"/> at the per-user location.
/// Failures are absorbed (corrupt file → defaults, unwritable dir → silent
/// no-op) because editor preferences are best-effort: a missing or broken
/// settings file should never block launch.
/// </summary>
public static class EditorSettingsIO
{
    private const string SettingsFileName = "editor.json";
    private const string AppFolderName = "RenderLab";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Returns the absolute path to the per-user settings file. Always
    /// returns a valid path — does not create the directory.
    /// </summary>
    public static string ResolveSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppFolderName, SettingsFileName);
    }

    /// <summary>
    /// Returns persisted settings if present and valid; otherwise the
    /// defaults. Never throws — corrupt files yield <see cref="EditorSettings.Default"/>.
    /// </summary>
    public static EditorSettings ReadOrDefault()
    {
        var path = ResolveSettingsPath();
        if (!File.Exists(path)) return EditorSettings.Default;
        try
        {
            var bytes = File.ReadAllBytes(path);
            return JsonSerializer.Deserialize<EditorSettings>(bytes, JsonOptions) ?? EditorSettings.Default;
        }
        catch
        {
            return EditorSettings.Default;
        }
    }

    /// <summary>
    /// Persists <paramref name="settings"/> to the per-user location.
    /// Best-effort: any I/O failure is swallowed (the editor should still
    /// close even when the settings folder is read-only).
    /// </summary>
    public static void Write(EditorSettings settings)
    {
        var path = ResolveSettingsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
            File.WriteAllBytes(path, bytes);
        }
        catch
        {
            // best-effort
        }
    }
}
