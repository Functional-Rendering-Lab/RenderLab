namespace RenderLab.Ui;

/// <summary>
/// The demos the shell can run. Used as the authoritative identifier for the
/// active demo (<see cref="AppUiModel.CurrentDemo"/>) and as the one-shot
/// switch request (<see cref="AppUiModel.RequestedDemo"/>) that the outer
/// <c>Program</c> loop consumes to tear down and spin up the next demo.
/// </summary>
public enum DemoId
{
    Triangle,
    GBuffer,
    Deferred,
}
