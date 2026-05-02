namespace RenderLab.Assets;

/// <summary>
/// CPU-side texture asset record: identity, display name, dimensions, encoding,
/// and tightly-packed pixel bytes (row-major, 4 bytes per pixel for the current
/// formats). Owned by an asset registry; scenes reference it indirectly through
/// <see cref="TextureId"/>.
/// </summary>
public sealed record TextureAsset(
    TextureId Id,
    string Name,
    int Width,
    int Height,
    TextureFormat Format,
    byte[] Pixels);
