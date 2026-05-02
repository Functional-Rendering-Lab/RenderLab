namespace RenderLab.Assets;

/// <summary>
/// Pure read interface from asset IDs to CPU-side asset records. Has no
/// knowledge of GPU resources — those are resolved by a separate shell-side
/// interface (see <c>IGpuAssetResolver</c> in <c>RenderLab.Gpu</c>). Pure code
/// (scene builders, papers, panels) only ever consumes this view.
/// </summary>
public interface IAssetCatalog
{
    MeshAsset GetMesh(MeshId id);
    bool TryGetMesh(MeshId id, out MeshAsset asset);
    IEnumerable<MeshAsset> AllMeshes { get; }

    TextureAsset GetTexture(TextureId id);
    bool TryGetTexture(TextureId id, out TextureAsset asset);
    IEnumerable<TextureAsset> AllTextures { get; }

    MaterialAsset GetMaterial(MaterialId id);
    bool TryGetMaterial(MaterialId id, out MaterialAsset asset);
    IEnumerable<MaterialAsset> AllMaterials { get; }

    /// <summary>True if this id refers to a built-in fallback that the
    /// registry refuses to remove. Used by editor UI to disable remove
    /// affordances on protected assets.</summary>
    bool IsBuiltin(MeshId id);
    bool IsBuiltin(TextureId id);
    bool IsBuiltin(MaterialId id);
}
