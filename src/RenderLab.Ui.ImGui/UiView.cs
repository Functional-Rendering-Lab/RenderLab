using ImGuiNET;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// What Dear ImGui still draws: the main menu bar, the dockspace the panels it has left are
/// placed by, and the Render Graph panel. Everything else in the editor is
/// <c>RenderLab.Editor</c>'s, and the two results are folded through one path by the shell.
/// <para>
/// This used to compose the whole interface. It goes when its last two occupants do, which is
/// the whole of what is left of the migration.
/// </para>
/// </summary>
public static class UiView
{
    public static UiViewResult Draw(AppUiModel app, FrameStats stats)
    {
        var appMessages = new List<AppUiMsg>();

        AppMenuBar.Draw(app, appMessages.Add);
        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        if (app.IsPanelVisible(PanelId.RenderGraph))
            RenderGraphDebugMenu.Draw(stats.ResolvedPasses);

        var io = ImGui.GetIO();
        var intent = new UiIntent(io.WantCaptureMouse, io.WantCaptureKeyboard);
        return new UiViewResult(appMessages, [], intent);
    }
}
