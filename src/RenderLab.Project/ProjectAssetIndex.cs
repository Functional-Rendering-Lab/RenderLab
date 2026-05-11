using System.Collections.Immutable;

namespace RenderLab.Project;

/// <summary>
/// Classification of a project file based on filename / extension. Drives icon
/// choice and double-click routing in the Project panel.
/// </summary>
public enum ProjectFileKind
{
    Scene,
    GltfModel,
    Image,
    Manifest,
    Json,
    Shader,
    Text,
    Other,
}

/// <summary>One file inside the project tree.</summary>
public sealed record ProjectFileEntry(
    string Name,
    string ProjectRelativePath,
    string AbsolutePath,
    ProjectFileKind Kind,
    long SizeBytes,
    DateTime LastWriteUtc);

/// <summary>One folder inside the project tree. Children are pre-sorted.</summary>
public sealed record ProjectFolderEntry(
    string Name,
    string ProjectRelativePath,
    string AbsolutePath,
    ImmutableArray<ProjectFolderEntry> Subfolders,
    ImmutableArray<ProjectFileEntry> Files);

/// <summary>
/// Immutable snapshot of a project's on-disk asset tree. Built by
/// <see cref="ProjectAssetScanner.Scan"/>; consumed by the Project panel.
/// </summary>
public sealed record ProjectAssetIndex(
    string ProjectRoot,
    ProjectFolderEntry Root,
    DateTime ScannedAtUtc)
{
    public static ProjectAssetIndex Empty(string projectRoot) => new(
        projectRoot,
        new ProjectFolderEntry(
            "",
            "",
            projectRoot,
            ImmutableArray<ProjectFolderEntry>.Empty,
            ImmutableArray<ProjectFileEntry>.Empty),
        DateTime.MinValue);
}
