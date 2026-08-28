using System.Numerics;
using ImGuiNET;
using RenderLab.Assets;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;
using Scene = RenderLab.Scene.Scene;

/// <summary>
/// Composes the app shell (main menu bar, dockspace) and debug panels into the
/// single entry point the Ui render pass invokes each frame. Calls
/// <c>ImGui.NewFrame</c> on the outside, draws menu bar + dockspace + every
/// panel (gated on <see cref="AppUiModel"/>), collects messages from the menu
/// (<see cref="AppUiMsg"/>) and the per-panel fragments (<see cref="UiMsg"/>),
/// and returns a <see cref="UiViewResult"/> for the shell to fold into the next
/// frame's model.
/// </summary>
public static class UiView
{
    public static UiViewResult Draw(AppUiModel app, UiModel model, Scene scene, IAssetCatalog catalog, FrameStats stats, ProjectAssetIndex projectIndex, AssetLibrary library)
    {
        var appMessages = new List<AppUiMsg>();
        var messages = new List<UiMsg>();
        Action<AppUiMsg> dispatchApp = appMessages.Add;
        Action<UiMsg> dispatch = messages.Add;

        AppMenuBar.Draw(app, dispatchApp);
        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        if (app.IsPanelVisible(PanelId.Visualization)) DrawVisualizationPanel(model.Viz, dispatch);
        if (app.IsPanelVisible(PanelId.Lighting))      LightingDebugMenu.Draw(model.Shading, model.LightingOnly, dispatch);
        if (app.IsPanelVisible(PanelId.RenderGraph))   RenderGraphDebugMenu.Draw(stats.ResolvedPasses);
        if (app.IsPanelVisible(PanelId.Scene))         ScenePanel.Draw(model, catalog, dispatch, dispatchApp);
        if (app.IsPanelVisible(PanelId.AssetBrowser))  AssetBrowserPanel.Draw(model, library, dispatch, dispatchApp);
        if (app.IsPanelVisible(PanelId.Project))       ProjectPanel.Draw(projectIndex, dispatchApp);
        if (app.IsPanelVisible(PanelId.Inspector))     InspectorPanel.Draw(model, catalog, library, dispatch, dispatchApp);

        var io = ImGui.GetIO();
        var intent = new UiIntent(io.WantCaptureMouse, io.WantCaptureKeyboard);
        return new UiViewResult(appMessages, messages, intent);
    }

    private static void DrawVisualizationPanel(VisualizationMode current, Action<UiMsg> dispatch)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 370), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 60), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Visualization"))
            VisualizationDebugMenu.Draw(current, dispatch);
        ImGui.End();
    }
}
