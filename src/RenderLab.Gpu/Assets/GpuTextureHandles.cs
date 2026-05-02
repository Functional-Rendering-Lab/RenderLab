using Silk.NET.Vulkan;

namespace RenderLab.Gpu.Assets;

/// <summary>
/// GPU-side handles for a registered texture: image view + sampler ready for a
/// combined-image-sampler descriptor write. Returned by
/// <see cref="IGpuAssetResolver.ResolveTexture"/> at the shell boundary.
/// The underlying <see cref="Image"/> and <see cref="Allocation"/> stay owned
/// by the registry.
/// </summary>
public readonly record struct GpuTextureHandles(ImageView View, Sampler Sampler);
