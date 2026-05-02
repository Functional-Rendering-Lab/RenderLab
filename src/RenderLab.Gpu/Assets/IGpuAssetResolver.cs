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
}
