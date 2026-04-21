using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

/// <summary>
/// Describes what a caller intends to do with a GPU allocation. Maps to a
/// <see cref="MemoryPropertyFlags"/> combination the driver understands.
/// </summary>
public enum MemoryIntent
{
    /// <summary>Device-local (<c>DEVICE_LOCAL</c>). GPU-only access. Render targets and
    /// static resources uploaded via a staging buffer.</summary>
    GpuOnly,

    /// <summary>Host-visible, host-coherent (<c>HOST_VISIBLE | HOST_COHERENT</c>).
    /// CPU writes, GPU reads. Vertex/index/uniform buffers updated per frame.</summary>
    CpuToGpu,
}

/// <summary>
/// Opaque handle to a block of GPU memory. Travels with the <see cref="Silk.NET.Vulkan.Buffer"/>
/// or <see cref="Image"/> it backs so lifetimes stay coupled at the type level.
/// </summary>
public readonly record struct Allocation(DeviceMemory Memory, ulong Size, uint MemoryType);

/// <summary>
/// The engine's single allocation surface. Owns the cached memory-type table and
/// performs one <c>vkAllocateMemory</c> per resource (no sub-allocation — see
/// <c>blogs/field-notes/choosing-a-vulkan-allocator</c> for the roadmap).
/// </summary>
public sealed class Allocator
{
    private readonly PhysicalDeviceMemoryProperties _memProps;

    public Allocator(Vk vk, PhysicalDevice physicalDevice)
    {
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out _memProps);
    }

    public unsafe (Silk.NET.Vulkan.Buffer buffer, Allocation alloc) AllocateBuffer(
        GpuState state, ulong size, BufferUsageFlags usage, MemoryIntent intent)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };

        if (state.Vk.CreateBuffer(state.Device, &bufferInfo, null, out var buffer) != Result.Success)
            throw new InvalidOperationException("Failed to create buffer.");

        state.Vk.GetBufferMemoryRequirements(state.Device, buffer, out var memReqs);
        var memoryType = FindMemoryType(memReqs.MemoryTypeBits, PropsFor(intent));

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = memoryType,
        };

        if (state.Vk.AllocateMemory(state.Device, &allocInfo, null, out var memory) != Result.Success)
            throw new InvalidOperationException("Failed to allocate buffer memory.");

        state.Vk.BindBufferMemory(state.Device, buffer, memory, 0);
        return (buffer, new Allocation(memory, memReqs.Size, memoryType));
    }

    public unsafe (Image image, Allocation alloc) AllocateImage(
        GpuState state, in ImageCreateInfo info, MemoryIntent intent)
    {
        Image image;
        fixed (ImageCreateInfo* pInfo = &info)
        {
            if (state.Vk.CreateImage(state.Device, pInfo, null, out image) != Result.Success)
                throw new InvalidOperationException("Failed to create image.");
        }

        state.Vk.GetImageMemoryRequirements(state.Device, image, out var memReqs);
        var memoryType = FindMemoryType(memReqs.MemoryTypeBits, PropsFor(intent));

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = memoryType,
        };

        if (state.Vk.AllocateMemory(state.Device, &allocInfo, null, out var memory) != Result.Success)
            throw new InvalidOperationException("Failed to allocate image memory.");

        state.Vk.BindImageMemory(state.Device, image, memory, 0);
        return (image, new Allocation(memory, memReqs.Size, memoryType));
    }

    public unsafe void DestroyBuffer(GpuState state, Silk.NET.Vulkan.Buffer buffer, Allocation alloc)
    {
        state.Vk.DestroyBuffer(state.Device, buffer, null);
        state.Vk.FreeMemory(state.Device, alloc.Memory, null);
    }

    public unsafe void DestroyImage(GpuState state, Image image, Allocation alloc)
    {
        state.Vk.DestroyImage(state.Device, image, null);
        state.Vk.FreeMemory(state.Device, alloc.Memory, null);
    }

    public unsafe void* Map(GpuState state, Allocation alloc)
    {
        void* mapped;
        if (state.Vk.MapMemory(state.Device, alloc.Memory, 0, alloc.Size, 0, &mapped) != Result.Success)
            throw new InvalidOperationException("Failed to map memory.");
        return mapped;
    }

    public unsafe void Unmap(GpuState state, Allocation alloc)
    {
        state.Vk.UnmapMemory(state.Device, alloc.Memory);
    }

    private static MemoryPropertyFlags PropsFor(MemoryIntent intent) => intent switch
    {
        MemoryIntent.GpuOnly => MemoryPropertyFlags.DeviceLocalBit,
        MemoryIntent.CpuToGpu => MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
        _ => throw new ArgumentOutOfRangeException(nameof(intent)),
    };

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        for (uint i = 0; i < _memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (_memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
                return i;
        }
        throw new InvalidOperationException($"No memory type satisfies flags {properties}.");
    }
}
