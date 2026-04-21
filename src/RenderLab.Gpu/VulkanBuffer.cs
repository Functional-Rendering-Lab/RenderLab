using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

/// <summary>
/// Creates and destroys Vulkan buffers backed by host-visible memory.
/// Thin convenience over <see cref="Allocator"/> — data is uploaded immediately
/// via mapped memory, no staging buffer.
/// </summary>
public static class VulkanBuffer
{
    /// <summary>
    /// Creates a host-visible buffer, uploads <paramref name="data"/> immediately, and
    /// returns the buffer paired with its <see cref="Allocation"/>.
    /// </summary>
    /// <returns>The buffer and backing allocation (pass both to <see cref="Destroy"/>).</returns>
    public static unsafe (Silk.NET.Vulkan.Buffer buffer, Allocation alloc) Create<T>(
        GpuState state, BufferUsageFlags usage, ReadOnlySpan<T> data) where T : unmanaged
    {
        var size = (ulong)(data.Length * sizeof(T));
        var (buffer, alloc) = state.Allocator.AllocateBuffer(state, size, usage, MemoryIntent.CpuToGpu);

        var mapped = state.Allocator.Map(state, alloc);
        fixed (T* src = data)
            System.Buffer.MemoryCopy(src, mapped, (long)size, (long)size);
        state.Allocator.Unmap(state, alloc);

        return (buffer, alloc);
    }

    public static void Destroy(GpuState state, Silk.NET.Vulkan.Buffer buffer, Allocation alloc) =>
        state.Allocator.DestroyBuffer(state, buffer, alloc);
}
