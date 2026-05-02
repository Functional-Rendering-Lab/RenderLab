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
            // No file picker yet — opens a hardcoded sample. Swap in a
            // real dialog when the lab needs arbitrary glTF input.
            if (ImGui.MenuItem("Import glTF (sample)"))
                dispatch(new AppUiMsg.RequestImportGltf("assets/box-textured.glb"));
            ImGui.Separator();
            if (ImGui.MenuItem("Exit", "Alt+F4"))
                dispatch(new AppUiMsg.RequestExit());
            ImGui.EndMenu();
        }

        if (includeViewMenu && ImGui.BeginMenu("View"))
        {
            ViewToggle("GPU Timings",   PanelId.GpuTimings,    app, dispatch);
            ViewToggle("Visualization", PanelId.Visualization, app, dispatch);
            ViewToggle("Camera",        PanelId.Camera,        app, dispatch);
            ViewToggle("Lighting",      PanelId.Lighting,      app, dispatch);
            ViewToggle("Render Graph",  PanelId.RenderGraph,   app, dispatch);
            ViewToggle("Scene",         PanelId.Scene,         app, dispatch);
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

    private static void ViewToggle(string label, PanelId id, AppUiModel app, Action<AppUiMsg> dispatch)
    {
        bool next = app.IsPanelVisible(id);
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
