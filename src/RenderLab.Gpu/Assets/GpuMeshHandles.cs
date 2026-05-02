using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace RenderLab.Gpu.Assets;

/// <summary>
/// GPU-side handles for a registered mesh: vertex/index buffers and index count.
/// Returned by <see cref="IGpuAssetResolver.ResolveMesh"/> at the shell boundary,
/// never stored in pure scene data.
/// </summary>
public readonly record struct GpuMeshHandles(VkBuffer VertexBuffer, VkBuffer IndexBuffer, uint IndexCount);
