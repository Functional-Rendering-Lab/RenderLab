using System.Numerics;
using ImGuiNET;
using RenderLab.Project;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Project-scoped inventory of usable assets, grouped by kind. Driven by the
/// pure <see cref="AssetLibrary"/> rather than the runtime GPU registry — so
/// entries survive scene swaps and exist even before any scene is loaded.
/// Read-only for now; rename / delete / drag-to-scene land in a later step.
/// </summary>
public static class AssetBrowserPanel
{
    public static void Draw(AssetLibrary library, Action<AppUiMsg> dispatchApp)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(380, 380), ImGuiCond.FirstUseEver);

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

        DrawSection("Meshes",     library, AssetKind.Mesh);
        DrawSection("Textures",   library, AssetKind.Texture);
        DrawSection("Materials",  library, AssetKind.Material);

        ImGui.End();
    }

    private static void DrawSection(string label, AssetLibrary library, AssetKind kind)
    {
        var entries = library.EntriesOfKind(kind).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!ImGui.TreeNodeEx($"{label} ({entries.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (var e in entries)
        {
            switch (e)
            {
                case FileAssetEntry f:
                    ImGui.Text($"{f.Name}");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"  {f.ProjectRelativePath}");
                    break;
                case MaterialAssetEntry m:
                    var tex = m.AlbedoTex is null ? "no tex" : "tex";
                    ImGui.Text($"{m.Name}");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"  [{tex}]  {m.ProjectRelativePath}");
                    break;
                default:
                    ImGui.Text(e.Name);
                    break;
            }
        }
        ImGui.TreePop();
    }
}
