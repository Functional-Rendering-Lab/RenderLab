namespace RenderLab.Assets;

/// <summary>
/// CPU-side mesh asset record: identity, display name, and indexed geometry.
/// Owned by an asset registry; the scene references it indirectly through
/// <see cref="MeshId"/>.
/// </summary>
public sealed record MeshAsset(MeshId Id, string Name, MeshData Data);
