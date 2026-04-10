using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using RenderLab.Gpu;

namespace RenderLab.Platform.Desktop;

/// <summary>
/// GLFW window wrapper using Silk.NET.Windowing.
/// Exposes a poll-loop interface — no OOP event callback patterns.
/// </summary>
public sealed class DesktopWindow : IPlatformWindow
{
    private readonly IWindow _window;
    private readonly IInputContext _input;
    private bool _resized;

    // Mouse state tracking
    private Vector2 _mousePos;
    private Vector2 _prevMousePos;
    private float _scrollAccum;

    public int Width => _window.Size.X;
    public int Height => _window.Size.Y;
    public bool IsClosing => _window.IsClosing;
    public bool WasResized => _resized;

    public void ClearResizeFlag() => _resized = false;

    private DesktopWindow(IWindow window, IInputContext input)
    {
        _window = window;
        _input = input;
        _window.Resize += _ => _resized = true;

        // Track scroll via callback — scroll events are transient
        if (_input.Mice.Count > 0)
        {
            var mouse = _input.Mice[0];
            mouse.Scroll += (_, wheel) => _scrollAccum += wheel.Y;
            _mousePos = mouse.Position;
            _prevMousePos = _mousePos;
        }
    }

    /// <summary>
    /// Creates and initializes a Vulkan-capable GLFW window.
    /// The window is visible immediately after creation.
    /// </summary>
    public static DesktopWindow Create(string title, int width, int height)
    {
        var options = WindowOptions.DefaultVulkan with
        {
            Title = title,
            Size = new Vector2D<int>(width, height),
            IsVisible = true,
        };

        var window = Window.Create(options);
        window.Initialize();

        var input = window.CreateInput();
        return new DesktopWindow(window, input);
    }

    public void DoEvents() => _window.DoEvents();

    /// <summary>
    /// Captures current mouse/keyboard state as an immutable snapshot, then resets per-frame accumulators.
    /// Call once per frame after <see cref="DoEvents"/>.
    /// </summary>
    public InputSnapshot PollInput()
    {
        var snapshot = default(InputSnapshot);

        if (_input.Mice.Count > 0)
        {
            var mouse = _input.Mice[0];
            _mousePos = mouse.Position;

            snapshot = new InputSnapshot(
                MousePosition: _mousePos,
                MouseDelta: _mousePos - _prevMousePos,
                ScrollDelta: _scrollAccum,
                LeftButtonDown: mouse.IsButtonPressed(MouseButton.Left),
                RightButtonDown: mouse.IsButtonPressed(MouseButton.Right),
                MiddleButtonDown: mouse.IsButtonPressed(MouseButton.Middle));

            _prevMousePos = _mousePos;
            _scrollAccum = 0;
        }

        return snapshot;
    }

    /// <summary>Returns the Vulkan instance extensions required by the windowing system (e.g. VK_KHR_win32_surface).</summary>
    public string[] GetRequiredVulkanExtensions()
    {
        var surface = _window.VkSurface
            ?? throw new InvalidOperationException("Window was not created with Vulkan support.");

        unsafe
        {
            var extensions = surface.GetRequiredExtensions(out var count);
            var result = new string[count];
            for (int i = 0; i < count; i++)
                result[i] = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)extensions[i])!;
            return result;
        }
    }

    /// <summary>Creates a Vulkan surface for this window. Called once during <see cref="VulkanDevice.Create"/>.</summary>
    public unsafe SurfaceKHR CreateVulkanSurface(Instance instance)
    {
        var surface = _window.VkSurface
            ?? throw new InvalidOperationException("Window was not created with Vulkan support.");

        var handle = surface.Create<AllocationCallbacks>(instance.ToHandle(), null);
        return handle.ToSurface();
    }

    public void Dispose()
    {
        _input.Dispose();
        _window.Reset();
        _window.Dispose();
    }
}
