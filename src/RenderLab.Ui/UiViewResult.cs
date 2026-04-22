namespace RenderLab.Ui;

/// <summary>
/// The view's output for one frame: app-shell messages (fold with
/// <see cref="AppUiUpdate.ApplyAll"/>), demo-scope messages (fold with
/// <see cref="UiUpdate.ApplyAll"/>), and the <see cref="UiIntent"/> that tells
/// the shell who wants the input.
/// </summary>
public sealed record UiViewResult(
    IReadOnlyList<AppUiMsg> AppMessages,
    IReadOnlyList<UiMsg> Messages,
    UiIntent Intent);
