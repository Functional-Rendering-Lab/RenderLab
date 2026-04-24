using Silk.NET.Input;

namespace RenderLab.Platform.Desktop;

/// <summary>
/// Polled keyboard state for a single frame. Produced by <see cref="DesktopWindow.PollKeyboard"/>.
/// </summary>
public readonly record struct KeyboardSnapshot(
    IReadOnlyList<char> TypedChars,
    IReadOnlyList<(Key Key, bool Down)> KeyEvents);
