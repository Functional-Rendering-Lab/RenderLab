using System.Numerics;
using ImGuiNET;
using RenderLab.Project;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Unity-style Project panel: tree of folders + files rooted at the active
/// project directory. Pure view over <see cref="ProjectAssetIndex"/>; emits
/// <see cref="AppUiMsg"/>s for refresh, open-scene, import, and reveal-in-OS.
/// File classification and tree shape come from the index; ImGui owns
/// expand state via its id stack so the panel stores nothing of its own.
/// </summary>
public static class ProjectPanel
{
    public static void Draw(ProjectAssetIndex index, Action<AppUiMsg> dispatchApp)
    {
        ImGui.SetNextWindowPos(new Vector2(400, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(380, 380), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Project"))
        {
            ImGui.End();
            return;
        }

        if (ImGui.Button("Refresh"))
            dispatchApp(new AppUiMsg.RequestRescanProject());
        ImGui.SameLine();
        var stamp = index.ScannedAtUtc == DateTime.MinValue
            ? "not scanned"
            : $"scanned {index.ScannedAtUtc.ToLocalTime():HH:mm:ss}";
        ImGui.TextDisabled(stamp);

        ImGui.Separator();

        if (string.IsNullOrEmpty(index.ProjectRoot))
        {
            ImGui.TextDisabled("(no project open)");
            ImGui.End();
            return;
        }

        DrawFolderChildren(index.Root, depth: 0, dispatchApp);

        ImGui.End();
    }

    private static void DrawFolder(ProjectFolderEntry f, int depth, Action<AppUiMsg> dispatch)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (depth == 0) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        bool open = ImGui.TreeNodeEx($"📁 {f.Name}##dir-{f.ProjectRelativePath}", flags);

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Reveal in Explorer"))
                dispatch(new AppUiMsg.RequestRevealInExplorer(f.AbsolutePath));
            if (ImGui.MenuItem("Copy project-relative path"))
                ImGui.SetClipboardText(f.ProjectRelativePath);
            ImGui.EndPopup();
        }

        if (open)
        {
            DrawFolderChildren(f, depth + 1, dispatch);
            ImGui.TreePop();
        }
    }

    private static void DrawFolderChildren(ProjectFolderEntry f, int depth, Action<AppUiMsg> dispatch)
    {
        foreach (var sub in f.Subfolders)
            DrawFolder(sub, depth, dispatch);
        foreach (var file in f.Files)
            DrawFile(file, dispatch);
    }

    private static void DrawFile(ProjectFileEntry f, Action<AppUiMsg> dispatch)
    {
        var label = $"{GlyphFor(f.Kind)} {f.Name}##file-{f.ProjectRelativePath}";
        ImGui.Selectable(label, false, ImGuiSelectableFlags.AllowDoubleClick);

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            DoubleClick(f, dispatch);

        if (ImGui.BeginPopupContextItem())
        {
            if (f.Kind == ProjectFileKind.Scene && ImGui.MenuItem("Open Scene"))
                dispatch(new AppUiMsg.RequestOpenScene(f.ProjectRelativePath));
            if (f.Kind == ProjectFileKind.GltfModel && ImGui.MenuItem("Import"))
                dispatch(new AppUiMsg.RequestImportGltf(f.AbsolutePath));
            if (ImGui.MenuItem("Reveal in Explorer"))
                dispatch(new AppUiMsg.RequestRevealInExplorer(f.AbsolutePath));
            if (ImGui.MenuItem("Copy project-relative path"))
                ImGui.SetClipboardText(f.ProjectRelativePath);
            ImGui.EndPopup();
        }
    }

    private static void DoubleClick(ProjectFileEntry f, Action<AppUiMsg> dispatch)
    {
        switch (f.Kind)
        {
            case ProjectFileKind.Scene:
                dispatch(new AppUiMsg.RequestOpenScene(f.ProjectRelativePath));
                break;
            case ProjectFileKind.GltfModel:
                dispatch(new AppUiMsg.RequestImportGltf(f.AbsolutePath));
                break;
        }
    }

    private static string GlyphFor(ProjectFileKind kind) => kind switch
    {
        ProjectFileKind.Scene     => "🎬",
        ProjectFileKind.GltfModel => "🧊",
        ProjectFileKind.Image     => "🖼",
        ProjectFileKind.Manifest  => "📜",
        ProjectFileKind.Json      => "{}",
        ProjectFileKind.Shader    => "⚡",
        ProjectFileKind.Text      => "📝",
        _                         => "📄",
    };
}
