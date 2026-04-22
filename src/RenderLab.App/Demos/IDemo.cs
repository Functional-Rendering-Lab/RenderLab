using RenderLab.Ui;

namespace RenderLab.App.Demos;

/// <summary>
/// A runnable demo owns its own window / GPU / ImGui for its lifetime. The outer
/// <c>Program</c> loop constructs one demo at a time, calls <see cref="Run"/>,
/// then disposes. The return value drives the process-internal demo picker:
/// a non-null <see cref="DemoId"/> asks the shell to tear down and spin up that
/// demo next; <c>null</c> exits the process.
/// </summary>
public interface IDemo : IDisposable
{
    DemoId? Run(AppUiModel initialApp);
}
