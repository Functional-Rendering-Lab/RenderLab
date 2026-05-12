using System.Collections.Immutable;

namespace RenderLab.Project;

/// <summary>
/// Walks a project root and produces an immutable <see cref="ProjectAssetIndex"/>.
/// File I/O only — no Vulkan, no registry. Unreadable subdirectories are
/// silently omitted; a missing root yields <see cref="ProjectAssetIndex.Empty"/>.
///
/// As a side effect, the scanner ensures every importable asset file has a
/// sibling <c>.meta</c> sidecar (created with a fresh GUID on first sight).
/// This is the single point where stable asset identity is assigned.
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
        if (fileName.EndsWith(".mat.json", StringComparison.OrdinalIgnoreCase))
            return ProjectFileKind.Material;
        if (fileName.EndsWith(".proc.meta", StringComparison.OrdinalIgnoreCase))
            return ProjectFileKind.Procedural;

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

    /// <summary>
    /// Whether the given file kind is an importable asset that should carry a
    /// <c>.meta</c> sidecar. Matches the kinds the Asset Browser surfaces.
    /// </summary>
    public static bool IsImportable(ProjectFileKind kind) => kind switch
    {
        ProjectFileKind.GltfModel => true,
        ProjectFileKind.Image     => true,
        ProjectFileKind.Material  => true,
        _ => false,
    };

    /// <summary>Maps an importable file kind to its asset-library kind.</summary>
    public static AssetKind ToAssetKind(ProjectFileKind kind) => kind switch
    {
        ProjectFileKind.GltfModel => AssetKind.Mesh,
        ProjectFileKind.Image     => AssetKind.Texture,
        ProjectFileKind.Material  => AssetKind.Material,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), $"{kind} is not importable"),
    };

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

                // .meta sidecars are accessed via their parent file — never
                // listed as standalone entries. .proc.meta is special: it IS
                // the asset (no sibling sidecar) so we let it through.
                if (fileName.EndsWith(AssetMetaIO.MetaExtension, StringComparison.OrdinalIgnoreCase)
                    && !fileName.EndsWith(".proc.meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                var kind = Classify(fileName);
                System.Guid? metaGuid = null;
                if (kind == ProjectFileKind.Procedural)
                {
                    var doc = ProceduralAssetIO.TryRead(f);
                    if (doc is not null) metaGuid = doc.Guid;
                }
                else if (IsImportable(kind))
                {
                    try
                    {
                        var doc = AssetMetaIO.ReadOrCreate(f, ToAssetKind(kind));
                        metaGuid = doc.Guid;
                    }
                    catch
                    {
                        // Couldn't write a .meta (read-only fs?) — surface the
                        // file anyway, just without a stable id.
                    }
                }

                files.Add(new ProjectFileEntry(
                    fileName,
                    ToRelative(projectRoot, f),
                    f,
                    kind,
                    info.Length,
                    info.LastWriteTimeUtc,
                    metaGuid));
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
