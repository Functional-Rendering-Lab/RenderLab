using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RenderLab.Gpu;

/// <summary>
/// The single mutable kernel. Contains all Vulkan state.
/// Passed explicitly — never global, never static, never ambient.
/// </summary>
public sealed class GpuState : IDisposable
{
    /// <summary>Double-buffered: 2 frames can be in flight simultaneously.</summary>
    public const int MaxFramesInFlight = 2;

    // ─── Device capabilities (queried once at creation) ───────────
    /// <summary>Immutable device properties and feature flags, queried once during <see cref="VulkanDevice.Create"/>.</summary>
    public required DeviceCapabilities Capabilities { get; init; }

    // ─── Vulkan API ─────────────────────────────────────────────────
    public required Vk Vk { get; init; }
    public required Instance Instance { get; init; }
    public required SurfaceKHR Surface { get; set; }
    public required PhysicalDevice PhysicalDevice { get; init; }
    public required Device Device { get; init; }
    public required Queue GraphicsQueue { get; init; }
    public required Queue PresentQueue { get; init; }
    public required uint GraphicsQueueFamily { get; init; }
    public required uint PresentQueueFamily { get; init; }

    // ─── KHR extensions ─────────────────────────────────────────────
    public required KhrSurface KhrSurface { get; init; }
    public required KhrSwapchain KhrSwapchain { get; init; }

    // ─── Swapchain ──────────────────────────────────────────────────
    public SwapchainKHR Swapchain { get; set; }
    public Image[] SwapchainImages { get; set; } = [];
    public ImageView[] SwapchainImageViews { get; set; } = [];
    public Format SwapchainFormat { get; set; }
    public Extent2D SwapchainExtent { get; set; }

    // ─── Commands ───────────────────────────────────────────────────
    public CommandPool CommandPool { get; set; }
    /// <summary>One command buffer per frame-in-flight, indexed by <see cref="CurrentFrame"/>.</summary>
    public CommandBuffer[] CommandBuffers { get; set; } = [];

    // ─── Per-frame synchronization ──────────────────────────────────
    /// <summary>Signaled when a swapchain image is acquired and ready to render into.</summary>
    public Semaphore[] ImageAvailableSemaphores { get; set; } = [];
    /// <summary>Signaled when rendering is complete and the image can be presented.</summary>
    public Semaphore[] RenderFinishedSemaphores { get; set; } = [];
    /// <summary>CPU-side fence: waited on before reusing a frame's command buffer.</summary>
    public Fence[] InFlightFences { get; set; } = [];

    // ─── Frame tracking ─────────────────────────────────────────────
    /// <summary>Ring-buffer index into per-frame arrays (0 to <see cref="MaxFramesInFlight"/>-1).</summary>
    public int CurrentFrame { get; set; }
    /// <summary>Set by the swapchain when the window is resized; triggers swapchain recreation.</summary>
    public bool FramebufferResized { get; set; }

    public void Dispose()
    {
        VulkanDevice.Destroy(this);
    }
}
