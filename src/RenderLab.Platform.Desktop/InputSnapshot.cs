using System.Numerics;

namespace RenderLab.Platform.Desktop;

/// <summary>
/// Polled input state for a single frame. Value type — no allocations.
/// Produced by <see cref="DesktopWindow.PollInput"/>, consumed by camera controllers.
/// </summary>
public readonly record struct InputSnapshot(
    Vector2 MousePosition,
    Vector2 MouseDelta,
    float ScrollDelta,
    bool LeftButtonDown,
    bool RightButtonDown,
    bool MiddleButtonDown);
