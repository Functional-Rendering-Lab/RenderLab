using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

/// <summary>
/// Creates offscreen images, depth images, and samplers for render targets.
/// All images use device-local memory (optimal tiling).
/// </summary>
public static class VulkanImage
{
    /// <summary>
    /// Creates a 2D color image usable as both a color attachment and a shader-sampled texture.
    /// Used for GBuffer targets and the HDR lighting output.
    /// </summary>
    /// <returns>Image, backing memory, and image view (all must be destroyed via <see cref="DestroyOffscreen"/>).</returns>
    public static unsafe (Image image, DeviceMemory memory, ImageView view) CreateOffscreen(
        GpuState state, Format format, uint width, uint height)
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        if (state.Vk.CreateImage(state.Device, &imageInfo, null, out var image) != Result.Success)
            throw new InvalidOperationException("Failed to create offscreen image.");

        state.Vk.GetImageMemoryRequirements(state.Device, image, out var memReqs);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(state, memReqs.MemoryTypeBits,
                MemoryPropertyFlags.DeviceLocalBit),
        };

        if (state.Vk.AllocateMemory(state.Device, &allocInfo, null, out var memory) != Result.Success)
            throw new InvalidOperationException("Failed to allocate offscreen image memory.");

        state.Vk.BindImageMemory(state.Device, image, memory, 0);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };

        if (state.Vk.CreateImageView(state.Device, &viewInfo, null, out var view) != Result.Success)
            throw new InvalidOperationException("Failed to create offscreen image view.");

        return (image, memory, view);
    }

    public static unsafe void DestroyOffscreen(
        GpuState state, Image image, DeviceMemory memory, ImageView view)
    {
        state.Vk.DestroyImageView(state.Device, view, null);
        state.Vk.DestroyImage(state.Device, image, null);
        state.Vk.FreeMemory(state.Device, memory, null);
    }

    /// <summary>
    /// Creates a linear-filtering sampler with clamp-to-edge addressing.
    /// Shared across all descriptor sets that sample render targets.
    /// </summary>
    public static unsafe Sampler CreateSampler(GpuState state)
    {
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipmapMode = SamplerMipmapMode.Linear,
            MaxLod = 1.0f,
        };

        if (state.Vk.CreateSampler(state.Device, &samplerInfo, null, out var sampler) != Result.Success)
            throw new InvalidOperationException("Failed to create sampler.");

        return sampler;
    }

    /// <summary>
    /// Finds the best supported depth format. Prefers D32_SFLOAT, falls back to
    /// D24_UNORM_S8_UINT (common on Android/mobile GPUs), then D16_UNORM.
    /// </summary>
    public static unsafe Format FindDepthFormat(Vk vk, PhysicalDevice physicalDevice)
    {
        ReadOnlySpan<Format> candidates = [Format.D32Sfloat, Format.D24UnormS8Uint, Format.D16Unorm];
        foreach (var format in candidates)
        {
            vk.GetPhysicalDeviceFormatProperties(physicalDevice, format, out var props);
            if ((props.OptimalTilingFeatures & FormatFeatureFlags.DepthStencilAttachmentBit) != 0)
                return format;
        }
        return Format.D32Sfloat; // last resort
    }

    /// <inheritdoc cref="FindDepthFormat(Vk, PhysicalDevice)"/>
    public static Format FindDepthFormat(GpuState state) =>
        FindDepthFormat(state.Vk, state.PhysicalDevice);

    /// <summary>
    /// Creates a depth image for depth testing. Queries the device for the best supported format.
    /// </summary>
    public static unsafe (Image image, DeviceMemory memory, ImageView view) CreateDepthImage(
        GpuState state, uint width, uint height)
    {
        return CreateDepthImage(state, width, height, FindDepthFormat(state));
    }

    /// <summary>
    /// Creates a depth image with an explicit format. Use <see cref="FindDepthFormat"/> to query support.
    /// </summary>
    public static unsafe (Image image, DeviceMemory memory, ImageView view) CreateDepthImage(
        GpuState state, uint width, uint height, Format depthFormat, bool samplable = false)
    {
        var usage = ImageUsageFlags.DepthStencilAttachmentBit;
        if (samplable) usage |= ImageUsageFlags.SampledBit;

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = depthFormat,
            Extent = new Extent3D(width, height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        if (state.Vk.CreateImage(state.Device, &imageInfo, null, out var image) != Result.Success)
            throw new InvalidOperationException("Failed to create depth image.");

        state.Vk.GetImageMemoryRequirements(state.Device, image, out var memReqs);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(state, memReqs.MemoryTypeBits,
                MemoryPropertyFlags.DeviceLocalBit),
        };

        if (state.Vk.AllocateMemory(state.Device, &allocInfo, null, out var memory) != Result.Success)
            throw new InvalidOperationException("Failed to allocate depth image memory.");

        state.Vk.BindImageMemory(state.Device, image, memory, 0);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = depthFormat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };

        if (state.Vk.CreateImageView(state.Device, &viewInfo, null, out var view) != Result.Success)
            throw new InvalidOperationException("Failed to create depth image view.");

        return (image, memory, view);
    }

    private static unsafe uint FindMemoryType(GpuState state, uint typeFilter, MemoryPropertyFlags properties)
    {
        state.Vk.GetPhysicalDeviceMemoryProperties(state.PhysicalDevice, out var memProps);

        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                return i;
        }

        throw new InvalidOperationException($"Failed to find suitable memory type for flags {properties}.");
    }
}
