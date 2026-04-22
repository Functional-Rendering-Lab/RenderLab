namespace RenderLab.Ui;

/// <summary>
/// The view's output to the rest of the frame: who wants the input. The shell
/// checks <see cref="WantCaptureMouse"/> before forwarding pointer events to
/// the camera controller or scene interaction, so a hovered UI panel can swallow
/// the input without the shell needing to know about ImGui or any specific UI
/// backend.
/// </summary>
public readonly record struct UiIntent(bool WantCaptureMouse, bool WantCaptureKeyboard)
{
    public static UiIntent None => default;
}
