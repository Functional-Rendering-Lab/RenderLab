using RenderLab.Assets;

namespace RenderLab.Scene;

/// <summary>
/// One thing rendered in a frame: a mesh referenced by id, its world placement,
/// and the Blinn-Phong material to shade it with. Pure value — GPU buffers are
/// resolved from <see cref="Mesh"/> at submit time via <c>IGpuAssetResolver</c>.
/// <see cref="Material"/> stays as <see cref="MaterialParams"/> for now and
/// becomes a <c>MaterialId</c> in Step F.
/// </summary>
public sealed record Drawable(
    string Name,
    MeshId Mesh,
    Transform Transform,
    MaterialParams Material);
