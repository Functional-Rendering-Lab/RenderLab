using System.Collections.Immutable;
using RenderLab.Assets;

namespace RenderLab.Project;

/// <summary>
/// Runtime mapping from registered asset ids to the symbolic
/// <see cref="AssetSourceDoc"/> they came from. Built up by
/// <c>SceneLoader</c> as the scene is materialised, extended by the
/// shell whenever the user imports a new asset (glTF), and consumed
/// by <see cref="SceneDocumentBuilder"/> on save so the same source
/// declaration round-trips back into the scene file.
/// </summary>
/// <remarks>
/// Materials are not tracked here — they are scene-scoped parameter
/// bundles, not file-backed, so the builder serialises them inline by
/// looking up the live <see cref="MaterialAsset"/> via the catalog.
/// </remarks>
public sealed record SceneAssetSources(
    ImmutableDictionary<MeshId, AssetSourceDoc> MeshSources,
    ImmutableDictionary<TextureId, AssetSourceDoc> TextureSources)
{
    public static SceneAssetSources Empty { get; } = new(
        ImmutableDictionary<MeshId, AssetSourceDoc>.Empty,
        ImmutableDictionary<TextureId, AssetSourceDoc>.Empty);

    public SceneAssetSources WithMesh(MeshId id, AssetSourceDoc source) =>
        this with { MeshSources = MeshSources.SetItem(id, source) };

    public SceneAssetSources WithTexture(TextureId id, AssetSourceDoc source) =>
        this with { TextureSources = TextureSources.SetItem(id, source) };

    public SceneAssetSources WithoutMesh(MeshId id) =>
        this with { MeshSources = MeshSources.Remove(id) };

    public SceneAssetSources WithoutTexture(TextureId id) =>
        this with { TextureSources = TextureSources.Remove(id) };
}
