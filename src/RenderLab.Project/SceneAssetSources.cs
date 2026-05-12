using System.Collections.Immutable;
using RenderLab.Assets;

namespace RenderLab.Project;

/// <summary>
/// Runtime map from registered asset ids back to the <see cref="AssetRef"/>
/// they were resolved from. Built up by <c>SceneLoader</c> as the scene is
/// materialised and consumed by <see cref="SceneDocumentBuilder"/> on save
/// so drawables round-trip back to the same stable references.
/// </summary>
public sealed record SceneAssetSources(
    ImmutableDictionary<MeshId, AssetRef> MeshSources,
    ImmutableDictionary<TextureId, AssetRef> TextureSources,
    ImmutableDictionary<MaterialId, AssetRef> MaterialSources)
{
    public static SceneAssetSources Empty { get; } = new(
        ImmutableDictionary<MeshId, AssetRef>.Empty,
        ImmutableDictionary<TextureId, AssetRef>.Empty,
        ImmutableDictionary<MaterialId, AssetRef>.Empty);

    public SceneAssetSources WithMesh(MeshId id, AssetRef r) =>
        this with { MeshSources = MeshSources.SetItem(id, r) };

    public SceneAssetSources WithTexture(TextureId id, AssetRef r) =>
        this with { TextureSources = TextureSources.SetItem(id, r) };

    public SceneAssetSources WithMaterial(MaterialId id, AssetRef r) =>
        this with { MaterialSources = MaterialSources.SetItem(id, r) };

    public SceneAssetSources WithoutMesh(MeshId id) =>
        this with { MeshSources = MeshSources.Remove(id) };

    public SceneAssetSources WithoutTexture(TextureId id) =>
        this with { TextureSources = TextureSources.Remove(id) };

    public SceneAssetSources WithoutMaterial(MaterialId id) =>
        this with { MaterialSources = MaterialSources.Remove(id) };
}
