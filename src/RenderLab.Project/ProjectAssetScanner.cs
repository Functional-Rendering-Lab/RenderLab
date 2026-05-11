using System.Collections.Immutable;

namespace RenderLab.Project;

/// <summary>
/// Walks a project root and produces an immutable <see cref="ProjectAssetIndex"/>.
/// File I/O only — no Vulkan, no registry. Unreadable subdirectories are
/// silently omitted; a missing root yields <see cref="ProjectAssetIndex.Empty"/>.
/// </summary>
public static class ProjectAssetScanner
{
    private static readonly HashSet<string> IgnoredFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".vs", ".git", ".idea" };

    public static ProjectAssetIndex Scan(string projectRoot)
    {
        if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
            return ProjectAssetIndex.Empty(projectRoot ?? "");

        try
        {
            var root = ScanFolder(projectRoot, projectRoot, name: "");
            return new ProjectAssetIndex(projectRoot, root, DateTime.UtcNow);
        }
        catch
        {
            return ProjectAssetIndex.Empty(projectRoot);
        }
    }

    public static ProjectFileKind Classify(string fileName)
    {
        if (string.Equals(fileName, "project.json", StringComparison.OrdinalIgnoreCase))
            return ProjectFileKind.Manifest;
        if (fileName.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase))
            return ProjectFileKind.Scene;

        var ext = Path.GetExtension(fileName);
        return ext.ToLowerInvariant() switch
        {
            ".glb" or ".gltf"                                  => ProjectFileKind.GltfModel,
            ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp"    => ProjectFileKind.Image,
            ".json"                                            => ProjectFileKind.Json,
            ".glsl" or ".vert" or ".frag" or ".comp" or ".spv" => ProjectFileKind.Shader,
            ".md" or ".txt"                                    => ProjectFileKind.Text,
            _                                                  => ProjectFileKind.Other,
        };
    }

    private static ProjectFolderEntry ScanFolder(string projectRoot, string absolutePath, string name)
    {
        var subfolders = ImmutableArray.CreateBuilder<ProjectFolderEntry>();
        var files = ImmutableArray.CreateBuilder<ProjectFileEntry>();

        string[] dirs;
        string[] filePaths;
        try
        {
            dirs = Directory.GetDirectories(absolutePath);
            filePaths = Directory.GetFiles(absolutePath);
        }
        catch
        {
            return new ProjectFolderEntry(
                name,
                ToRelative(projectRoot, absolutePath),
                absolutePath,
                ImmutableArray<ProjectFolderEntry>.Empty,
                ImmutableArray<ProjectFileEntry>.Empty);
        }

        Array.Sort(dirs, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
        Array.Sort(filePaths, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));

        foreach (var d in dirs)
        {
            var dirName = Path.GetFileName(d);
            if (IsIgnored(dirName)) continue;
            try
            {
                subfolders.Add(ScanFolder(projectRoot, d, dirName));
            }
            catch
            {
                // omit unreadable subdir
            }
        }

        foreach (var f in filePaths)
        {
            try
            {
                var info = new FileInfo(f);
                var fileName = info.Name;
                files.Add(new ProjectFileEntry(
                    fileName,
                    ToRelative(projectRoot, f),
                    f,
                    Classify(fileName),
                    info.Length,
                    info.LastWriteTimeUtc));
            }
            catch
            {
                // omit unreadable file
            }
        }

        return new ProjectFolderEntry(
            name,
            ToRelative(projectRoot, absolutePath),
            absolutePath,
            subfolders.ToImmutable(),
            files.ToImmutable());
    }

    private static bool IsIgnored(string dirName)
        => IgnoredFolders.Contains(dirName) || dirName.StartsWith('.');

    private static string ToRelative(string root, string absolute)
    {
        if (string.Equals(root, absolute, StringComparison.OrdinalIgnoreCase))
            return "";
        var rel = Path.GetRelativePath(root, absolute);
        return rel.Replace('\\', '/');
    }
}
