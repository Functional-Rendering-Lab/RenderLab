using RenderLab.Assets;

namespace RenderLab.Scene;

/// <summary>
/// One thing rendered in a frame: a mesh referenced by id, its world placement,
/// the Blinn-Phong material to shade it with, and an optional albedo texture.
/// Pure value — GPU buffers and image views are resolved from
/// <see cref="Mesh"/> / <see cref="AlbedoMap"/> at submit time via
/// <c>IGpuAssetResolver</c>. <see cref="AlbedoMap"/> defaults to
/// <see cref="TextureId.None"/>, which the resolver maps to a built-in 1×1
/// white image so the shader can sample unconditionally.
/// <see cref="Material"/> stays as <see cref="MaterialParams"/> for now and
/// becomes a <c>MaterialId</c> in Step F.
/// </summary>
public sealed record Drawable(
    string Name,
    MeshId Mesh,
    Transform Transform,
    MaterialParams Material,
    TextureId AlbedoMap);
