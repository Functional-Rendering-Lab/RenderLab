using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

/// <summary>
/// Creates and destroys Vulkan buffers with host-visible, host-coherent memory.
/// Data is uploaded immediately via mapped memory (no staging buffer).
/// </summary>
public static class VulkanBuffer
{
    /// <summary>
    /// Creates a buffer, allocates host-visible memory, and uploads <paramref name="data"/> immediately.
    /// Suitable for vertex/index buffers that don't require device-local memory.
    /// </summary>
    /// <returns>The buffer and its backing memory (both must be destroyed via <see cref="Destroy"/>).</returns>
    public static unsafe (Silk.NET.Vulkan.Buffer buffer, DeviceMemory memory) Create<T>(
        GpuState state, BufferUsageFlags usage, ReadOnlySpan<T> data) where T : unmanaged
    {
        var size = (ulong)(data.Length * sizeof(T));

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

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(state, memReqs.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit),
        };

        if (state.Vk.AllocateMemory(state.Device, &allocInfo, null, out var memory) != Result.Success)
            throw new InvalidOperationException("Failed to allocate buffer memory.");

        state.Vk.BindBufferMemory(state.Device, buffer, memory, 0);

        // Upload data
        void* mapped;
        state.Vk.MapMemory(state.Device, memory, 0, size, 0, &mapped);
        fixed (T* src = data)
            System.Buffer.MemoryCopy(src, mapped, (long)size, (long)size);
        state.Vk.UnmapMemory(state.Device, memory);

        return (buffer, memory);
    }

    public static unsafe void Destroy(GpuState state, Silk.NET.Vulkan.Buffer buffer, DeviceMemory memory)
    {
        state.Vk.DestroyBuffer(state.Device, buffer, null);
        state.Vk.FreeMemory(state.Device, memory, null);
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
