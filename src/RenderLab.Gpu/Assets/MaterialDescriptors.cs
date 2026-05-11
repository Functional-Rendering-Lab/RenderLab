using RenderLab.Assets;
using Silk.NET.Vulkan;

namespace RenderLab.Gpu.Assets;

/// <summary>
/// Per-texture descriptor set cache for the GBuffer material slot. Each unique
/// <see cref="TextureId"/> resolves to one combined-image-sampler descriptor
/// set, allocated lazily and reused across frames (textures are read-only, so
/// a single set is safe even with frames in flight).
/// </summary>
public sealed class MaterialDescriptors : IDisposable
{
    private readonly GpuState _gpu;
    private readonly IGpuAssetResolver _resolver;
    private readonly DescriptorSetLayout _layout;
    private readonly DescriptorPool _pool;
    private readonly Dictionary<int, DescriptorSet> _cache = new();

    public MaterialDescriptors(GpuState gpu, IGpuAssetResolver resolver, DescriptorSetLayout layout, uint maxTextures)
    {
        _gpu = gpu;
        _resolver = resolver;
        _layout = layout;
        _pool = VulkanDescriptors.CreatePool(gpu, maxTextures);
    }

    /// <summary>
    /// Returns the descriptor set bound to <paramref name="id"/>'s texture,
    /// allocating and writing it on first use. <see cref="TextureId.None"/> is
    /// resolved by the registry to the built-in white fallback, so the same
    /// cache slot is reused for every untextured drawable.
    /// </summary>
    public unsafe DescriptorSet GetOrAllocate(TextureId id)
    {
        // Cache by the resolved key so None and the white fallback share a slot.
        var key = id.IsNone ? 0 : id.Value;
        if (_cache.TryGetValue(key, out var existing))
            return existing;

        var layout = _layout;
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        DescriptorSet set;
        if (_gpu.Vk.AllocateDescriptorSets(_gpu.Device, &allocInfo, &set) != Result.Success)
            throw new InvalidOperationException("Failed to allocate material descriptor set (pool exhausted?).");

        var handles = _resolver.ResolveTexture(id);
        var imageInfo = new DescriptorImageInfo
        {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = handles.View,
            Sampler = handles.Sampler,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &imageInfo,
        };
        _gpu.Vk.UpdateDescriptorSets(_gpu.Device, 1, &write, 0, null);

        _cache[key] = set;
        return set;
    }

    /// <summary>
    /// Drop the cached descriptor set for <paramref name="id"/> so that a
    /// subsequent <see cref="GetOrAllocate"/> rebinds against a fresh image
    /// view. Call this after the registry removes a texture; the underlying
    /// descriptor set stays in the pool (Vulkan allows orphaned sets) and is
    /// reclaimed when the pool itself is destroyed.
    /// </summary>
    public void InvalidateTexture(TextureId id)
    {
        var key = id.IsNone ? 0 : id.Value;
        _cache.Remove(key);
    }

    /// <summary>
    /// Drops every cached descriptor set. Call after the registry resets
    /// for a scene swap (and after <c>vkDeviceWaitIdle</c>) so subsequent
    /// <see cref="GetOrAllocate"/> calls rebind against the fresh image
    /// views. The pool keeps the orphaned sets — they're reclaimed when
    /// the pool is destroyed.
    /// </summary>
    public void InvalidateAll() => _cache.Clear();

    public unsafe void Dispose()
    {
        _gpu.Vk.DestroyDescriptorPool(_gpu.Device, _pool, null);
        _cache.Clear();
    }
}
