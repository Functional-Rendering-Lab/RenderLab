using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using RenderLab.Gpu;
using VkResult = Silk.NET.Vulkan.Result;

namespace RenderLab.Platform.Android;

/// <summary>
/// Android implementation of <see cref="IPlatformWindow"/> backed by an ANativeWindow
/// obtained from a SurfaceView's Surface via JNI.
/// </summary>
public sealed class AndroidWindow : IPlatformWindow
{
    private nint _nativeWindow;
    private volatile bool _closing;
    private volatile bool _resized;
    private volatile bool _surfaceInvalidated;
    private volatile int _width;
    private volatile int _height;

    public int Width => _width;
    public int Height => _height;
    public bool IsClosing => _closing;
    public bool WasResized => _resized;
    /// <summary>True when the underlying ANativeWindow changed and the Vulkan surface must be recreated.</summary>
    public bool SurfaceInvalidated => _surfaceInvalidated;

    public void ClearResizeFlag() => _resized = false;
    public void ClearSurfaceInvalidated() => _surfaceInvalidated = false;

    public AndroidWindow(nint nativeWindow, int width, int height)
    {
        _nativeWindow = nativeWindow;
        _width = width;
        _height = height;
    }

    public void UpdateSurface(nint nativeWindow, int width, int height)
    {
        if (nativeWindow != _nativeWindow)
        {
            _nativeWindow = nativeWindow;
            _surfaceInvalidated = true;
        }

        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            _resized = true;
        }
    }

    public void DoEvents()
    {
        // Event dispatching is handled by the Android Activity looper.
        // Nothing to poll here — SurfaceHolder callbacks drive state changes.
    }

    public string[] GetRequiredVulkanExtensions() =>
        ["VK_KHR_surface", "VK_KHR_android_surface"];

    public unsafe SurfaceKHR CreateVulkanSurface(Instance instance)
    {
        var vk = Vk.GetApi();

        if (!vk.TryGetInstanceExtension(instance, out KhrAndroidSurface androidSurfaceExt))
            throw new InvalidOperationException(
                "VK_KHR_android_surface extension not available.");

        var createInfo = new AndroidSurfaceCreateInfoKHR
        {
            SType = StructureType.AndroidSurfaceCreateInfoKhr,
            Window = (nint*)_nativeWindow,
        };

        if (androidSurfaceExt.CreateAndroidSurface(instance, &createInfo, null, out var surface) != VkResult.Success)
            throw new InvalidOperationException("Failed to create Android Vulkan surface.");

        return surface;
    }

    public void RequestClose() => _closing = true;

    public void Dispose()
    {
        // ANativeWindow lifetime is managed by the SurfaceView — we don't release it.
    }
}
