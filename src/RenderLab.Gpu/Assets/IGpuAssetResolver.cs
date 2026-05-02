using RenderLab.Assets;

namespace RenderLab.Gpu.Assets;

/// <summary>
/// Shell-only resolver from asset IDs to live GPU handles. Pure code consumes
/// <see cref="IAssetCatalog"/>; recording code (papers, passes) reaches for this
/// at the moment of submission.
/// </summary>
public interface IGpuAssetResolver
{
    GpuMeshHandles ResolveMesh(MeshId id);

    /// <summary>
    /// Returns view + sampler for the given texture, falling back to a built-in
    /// 1×1 white image when <paramref name="id"/> is <see cref="TextureId.None"/>.
    /// Shaders can therefore sample unconditionally without a per-draw branch.
    /// </summary>
    GpuTextureHandles ResolveTexture(TextureId id);
}
