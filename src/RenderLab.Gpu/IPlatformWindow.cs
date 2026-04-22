using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

/// <summary>
/// Platform-agnostic window abstraction for Vulkan surface creation and event polling.
/// Desktop implements via GLFW (Silk.NET.Windowing).
/// </summary>
public interface IPlatformWindow : IDisposable
{
    int Width { get; }
    int Height { get; }
    bool IsClosing { get; }
    bool WasResized { get; }
    void ClearResizeFlag();
    void DoEvents();
    string[] GetRequiredVulkanExtensions();
    unsafe SurfaceKHR CreateVulkanSurface(Instance instance);
}
