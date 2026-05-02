using RenderLab.Assets;

namespace RenderLab.Scene;

/// <summary>
/// One thing rendered in a frame: a mesh and a material referenced by id, plus
/// world placement. Pure value — both <see cref="Mesh"/> and
/// <see cref="Material"/> are resolved at submit time (mesh via
/// <c>IGpuAssetResolver</c>, material via <c>IAssetCatalog</c>) so the scene
/// snapshot stays GPU-free. <see cref="MaterialId.None"/> resolves to a
/// built-in default Blinn-Phong material so render code can dereference
/// unconditionally.
/// </summary>
public sealed record Drawable(
    string Name,
    MeshId Mesh,
    Transform Transform,
    MaterialId Material);
