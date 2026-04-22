using System.Numerics;
using ImGuiNET;
using RenderLab.Ui;

namespace RenderLab.Debug;

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
    public static UiViewResult Draw(AppUiModel app, UiModel model, FrameStats stats)
    {
        var appMessages = new List<AppUiMsg>();
        var messages = new List<UiMsg>();
        Action<AppUiMsg> dispatchApp = appMessages.Add;
        Action<UiMsg> dispatch = messages.Add;

        AppMenuBar.Draw(app, dispatchApp);
        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        if (app.ShowGpuTimings)    DrawGpuTimingsPanel(stats);
        if (app.ShowVisualization) DrawVisualizationPanel(model.Viz, dispatch);
        if (app.ShowCamera)        FreeCameraDebugMenu.Draw(model.Camera, dispatch);
        if (app.ShowLighting)      LightingDebugMenu.Draw(model.KeyLight, model.Shading, model.LightingOnly, dispatch);
        if (app.ShowSphere)        SphereDebugMenu.Draw(model.MeshTransform, model.Material, dispatch);
        if (app.ShowRenderGraph)   RenderGraphDebugMenu.Draw(stats.ResolvedPasses);

        var io = ImGui.GetIO();
        var intent = new UiIntent(io.WantCaptureMouse, io.WantCaptureKeyboard);
        return new UiViewResult(appMessages, messages, intent);
    }

    private static void DrawGpuTimingsPanel(FrameStats stats)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 140), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("GPU Timings"))
        {
            ImGui.End();
            return;
        }

        float total = 0;
        for (int i = 0; i < stats.TimestampMillis.Count; i++)
        {
            ImGui.Text($"{stats.TimestampLabels[i]}: {stats.TimestampMillis[i]:F3} ms");
            total += (float)stats.TimestampMillis[i];
        }
        ImGui.Separator();
        ImGui.Text($"Total GPU: {total:F3} ms");

        float dt = stats.DeltaSeconds;
        ImGui.Text($"Frame: {dt * 1000:F1} ms ({(dt > 0 ? 1.0f / dt : 0):F0} FPS)");

        ImGui.End();
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
