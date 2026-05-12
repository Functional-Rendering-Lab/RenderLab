namespace RenderLab.Project;

/// <summary>
/// Stable, filesystem-independent handle to a project asset. Scenes and other
/// engine data reference assets by <see cref="Guid"/> + optional sub-asset
/// selector, so renaming or moving the underlying file does not break the
/// reference. The <see cref="Guid"/> is assigned the first time the asset is
/// seen by the scanner and persisted in a sibling <c>.meta</c> sidecar.
/// </summary>
/// <param name="Guid">Identity of the asset; matches a <c>.meta</c> on disk.</param>
/// <param name="Sub">Optional sub-asset selector inside a multi-asset file
/// (e.g. <c>"mesh:0"</c> for the first mesh inside a glTF). <c>null</c> means
/// the file's primary/default sub-asset.</param>
public readonly record struct AssetRef(System.Guid Guid, string? Sub = null)
{
    public override string ToString() =>
        Sub is null ? Guid.ToString("D") : $"{Guid:D}#{Sub}";
}

/// <summary>Top-level classification used by the asset library and meta sidecars.</summary>
public enum AssetKind
{
    Mesh,
    Texture,
    Material,
    Procedural,
}
