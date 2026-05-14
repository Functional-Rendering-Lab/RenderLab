using ImGuiNET;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Main menu bar: File / View. Dispatches <see cref="AppUiMsg"/>s that the
/// shell folds into <see cref="AppUiModel"/>. Paired with
/// <c>ImGui.DockSpaceOverViewport</c> in the host view for docking.
/// </summary>
public static class AppMenuBar
{
    public static void Draw(AppUiModel app, Action<AppUiMsg> dispatch, bool includeViewMenu = true)
    {
        if (!ImGui.BeginMainMenuBar()) return;

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Project…"))
                dispatch(new AppUiMsg.RequestNewProjectDialog());
            if (ImGui.MenuItem("Open Project…"))
                dispatch(new AppUiMsg.RequestOpenProjectDialog());

            bool sceneMenuEnabled = app.AvailableScenes.Length > 0;
            if (ImGui.BeginMenu("Open Scene", sceneMenuEnabled))
            {
                foreach (var path in app.AvailableScenes)
                {
                    bool active = string.Equals(path, app.ActiveScenePath, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.MenuItem(path, "", active))
                        dispatch(new AppUiMsg.RequestOpenScene(path));
                }
                ImGui.EndMenu();
            }

            bool hasActive = !string.IsNullOrEmpty(app.ActiveScenePath);
            if (ImGui.MenuItem("Reload Scene", "", false, hasActive))
                dispatch(new AppUiMsg.RequestReloadScene());

            ImGui.Separator();

            string saveLabel = hasActive
                ? (app.SceneDirty ? $"Save Scene ({app.ActiveScenePath})*" : $"Save Scene ({app.ActiveScenePath})")
                : "Save Scene";
            if (ImGui.MenuItem(saveLabel, "Ctrl+S", false, hasActive))
                dispatch(new AppUiMsg.RequestSaveScene());
            if (ImGui.MenuItem("Save Scene As…", "", false, hasActive))
                dispatch(new AppUiMsg.RequestSaveSceneAs());

            ImGui.Separator();
            if (ImGui.MenuItem("Import glTF…"))
                dispatch(new AppUiMsg.RequestImportGltfDialog());

            ImGui.Separator();
            if (ImGui.MenuItem("Exit", "Alt+F4"))
                dispatch(new AppUiMsg.RequestExit());
            ImGui.EndMenu();
        }

        if (includeViewMenu && ImGui.BeginMenu("View"))
        {
            ViewToggle("GPU Timings",   PanelId.GpuTimings,    app, dispatch);
            ViewToggle("Visualization", PanelId.Visualization, app, dispatch);
            ViewToggle("Lighting",      PanelId.Lighting,      app, dispatch);
            ViewToggle("Render Graph",  PanelId.RenderGraph,   app, dispatch);
            ViewToggle("Scene",         PanelId.Scene,         app, dispatch);
            ViewToggle("Asset Browser", PanelId.AssetBrowser,  app, dispatch);
            ViewToggle("Project",       PanelId.Project,       app, dispatch);
            ViewToggle("Inspector",     PanelId.Inspector,     app, dispatch);
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
}
