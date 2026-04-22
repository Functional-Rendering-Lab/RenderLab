using ImGuiNET;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Main menu bar shared across demos: File / View / Demo. Dispatches
/// <see cref="AppUiMsg"/>s that the shell folds into <see cref="AppUiModel"/>.
/// Paired with <c>ImGui.DockSpaceOverViewport</c> in the host view for docking.
/// </summary>
public static class AppMenuBar
{
    public static void Draw(AppUiModel app, Action<AppUiMsg> dispatch, bool includeViewMenu = true)
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Exit", "Alt+F4"))
                dispatch(new AppUiMsg.RequestExit());
            ImGui.EndMenu();
        }

        if (includeViewMenu && ImGui.BeginMenu("View"))
        {
            ViewToggle("GPU Timings",   PanelId.GpuTimings,    app.ShowGpuTimings,    dispatch);
            ViewToggle("Visualization", PanelId.Visualization, app.ShowVisualization, dispatch);
            ViewToggle("Camera",        PanelId.Camera,        app.ShowCamera,        dispatch);
            ViewToggle("Lighting",      PanelId.Lighting,      app.ShowLighting,      dispatch);
            ViewToggle("Sphere",        PanelId.Sphere,        app.ShowSphere,        dispatch);
            ViewToggle("Render Graph",  PanelId.RenderGraph,   app.ShowRenderGraph,   dispatch);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Demo"))
        {
            DemoEntry("Triangle", DemoId.Triangle, app.CurrentDemo, dispatch);
            DemoEntry("GBuffer",  DemoId.GBuffer,  app.CurrentDemo, dispatch);
            DemoEntry("Deferred", DemoId.Deferred, app.CurrentDemo, dispatch);
            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    private static void ViewToggle(string label, PanelId id, bool visible, Action<AppUiMsg> dispatch)
    {
        bool next = visible;
        if (ImGui.MenuItem(label, "", ref next))
            dispatch(new AppUiMsg.SetPanelVisible(id, next));
    }

    private static void DemoEntry(string label, DemoId id, DemoId current, Action<AppUiMsg> dispatch)
    {
        bool selected = id == current;
        if (ImGui.MenuItem(label, "", selected, !selected))
            dispatch(new AppUiMsg.RequestSwitchDemo(id));
    }
}
