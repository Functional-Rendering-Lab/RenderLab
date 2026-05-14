using System.Numerics;
using ImGuiNET;
using RenderLab.Project;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Project-scoped inventory of usable assets, grouped by kind. Clicking a row
/// emits a <see cref="UiMsg.Select"/> — the Inspector renders the editor.
/// Rename / Delete stay here because they are list operations (file moves /
/// removal) rather than item-property edits.
/// </summary>
public static class AssetBrowserPanel
{
    // Per-row in-progress rename drafts, keyed by entry guid. ImGui
    // popups are id-scoped but their input buffers aren't — we need the
    // draft to survive across frames while the modal is open.
    private static readonly Dictionary<Guid, string> _renameDrafts = new();

    public static void Draw(UiModel ui, AssetLibrary library,
        Action<UiMsg> dispatch, Action<AppUiMsg> dispatchApp)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(380, 420), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Asset Browser"))
        {
            ImGui.End();
            return;
        }

        var stamp = library.ScannedAtUtc == DateTime.MinValue
            ? "not scanned"
            : $"scanned {library.ScannedAtUtc.ToLocalTime():HH:mm:ss}";
        ImGui.TextDisabled(stamp);
        ImGui.Separator();

        DrawSection("Meshes",    ui, library, AssetKind.Mesh,    dispatch, dispatchApp);
        DrawSection("Textures",  ui, library, AssetKind.Texture, dispatch, dispatchApp);
        DrawSection("Materials", ui, library, AssetKind.Material, dispatch, dispatchApp);

        ImGui.End();
    }

    private static void DrawSection(string label, UiModel ui, AssetLibrary library, AssetKind kind,
        Action<UiMsg> dispatch, Action<AppUiMsg> dispatchApp)
    {
        var entries = library.EntriesOfKind(kind).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!ImGui.TreeNodeEx($"{label} ({entries.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (var e in entries)
        {
            ImGui.PushID(e.Guid.ToString("N"));
            DrawRow(e, ui, dispatch, dispatchApp);
            ImGui.PopID();
        }
        ImGui.TreePop();
    }

    private static void DrawRow(AssetEntry e, UiModel ui,
        Action<UiMsg> dispatch, Action<AppUiMsg> dispatchApp)
    {
        var selected = IsSelected(ui.Selection, e);
        if (ImGui.Selectable($"{e.Name}##sel{e.Guid:N}", selected))
            dispatch(new UiMsg.Select(SelectionFor(e)));

        ImGui.SameLine();
        ImGui.TextDisabled(SecondaryLabel(e));
        ImGui.SameLine();
        DrawRenameButton(e, dispatchApp);
        ImGui.SameLine();
        DrawDeleteButton(e, dispatchApp);
    }

    private static Selection SelectionFor(AssetEntry e) => e switch
    {
        MaterialAssetEntry m => new Selection.MaterialAsset(m.Guid),
        _ when e.Kind == AssetKind.Mesh    => new Selection.MeshAsset(e.Guid),
        _ when e.Kind == AssetKind.Texture => new Selection.TextureAsset(e.Guid),
        _                                  => Selection.Empty,
    };

    private static bool IsSelected(Selection s, AssetEntry e) => s switch
    {
        Selection.MaterialAsset m => m.Guid == e.Guid,
        Selection.MeshAsset me    => me.Guid == e.Guid,
        Selection.TextureAsset t  => t.Guid == e.Guid,
        _                         => false,
    };

    private static string SecondaryLabel(AssetEntry e) => e switch
    {
        MaterialAssetEntry m  => $"  {(m.AlbedoTex is null ? "no tex" : "tex")}  {m.ProjectRelativePath}",
        FileAssetEntry f      => $"  {f.ProjectRelativePath}",
        ProceduralAssetEntry p => $"  ({p.Generator})",
        _                     => string.Empty,
    };

    private static void DrawRenameButton(AssetEntry e, Action<AppUiMsg> dispatchApp)
    {
        if (ImGui.SmallButton("Rename"))
        {
            _renameDrafts[e.Guid] = e.Name;
            ImGui.OpenPopup("rename-asset");
        }

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("rename-asset", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Rename '{e.Name}'");
            ImGui.TextDisabled("Extension and GUID are preserved.");
            ImGui.Separator();

            if (!_renameDrafts.TryGetValue(e.Guid, out var draft))
                draft = e.Name;

            if (ImGui.InputText("##rename", ref draft, 128, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                dispatchApp(new AppUiMsg.RequestRenameAsset(e.Guid, draft));
                _renameDrafts.Remove(e.Guid);
                ImGui.CloseCurrentPopup();
            }
            else
            {
                _renameDrafts[e.Guid] = draft;
            }

            bool canApply = !string.IsNullOrWhiteSpace(draft)
                            && !string.Equals(draft.Trim(), e.Name, StringComparison.Ordinal);
            if (!canApply) ImGui.BeginDisabled();
            if (ImGui.Button("Rename", new Vector2(120, 0)))
            {
                dispatchApp(new AppUiMsg.RequestRenameAsset(e.Guid, draft));
                _renameDrafts.Remove(e.Guid);
                ImGui.CloseCurrentPopup();
            }
            if (!canApply) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _renameDrafts.Remove(e.Guid);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private static void DrawDeleteButton(AssetEntry e, Action<AppUiMsg> dispatchApp)
    {
        if (ImGui.SmallButton("Delete"))
            ImGui.OpenPopup("confirm-delete");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("confirm-delete", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Delete '{e.Name}' from disk?");
            ImGui.TextDisabled("Source + .meta sidecar will be removed.");
            ImGui.Separator();
            if (ImGui.Button("Delete", new Vector2(120, 0)))
            {
                dispatchApp(new AppUiMsg.RequestDeleteAsset(e.Guid));
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}
