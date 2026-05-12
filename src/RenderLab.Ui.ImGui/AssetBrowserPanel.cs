using System.Numerics;
using ImGuiNET;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Project-scoped inventory of usable assets, grouped by kind. Driven by the
/// pure <see cref="AssetLibrary"/> rather than the runtime GPU registry — so
/// entries survive scene swaps and exist even before any scene is loaded.
/// Materials carry an inline editor that round-trips through their
/// <c>.mat.json</c> sidecar.
/// </summary>
public static class AssetBrowserPanel
{
    public static void Draw(AssetLibrary library, Action<AppUiMsg> dispatchApp)
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

        DrawSection("Meshes",    library, AssetKind.Mesh,    dispatchApp);
        DrawSection("Textures",  library, AssetKind.Texture, dispatchApp);
        DrawSection("Materials", library, AssetKind.Material, dispatchApp);

        ImGui.End();
    }

    private static void DrawSection(string label, AssetLibrary library, AssetKind kind, Action<AppUiMsg> dispatchApp)
    {
        var entries = library.EntriesOfKind(kind).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!ImGui.TreeNodeEx($"{label} ({entries.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (var e in entries)
        {
            switch (e)
            {
                case MaterialAssetEntry m:
                    DrawMaterialRow(m, library, dispatchApp);
                    break;
                case FileAssetEntry f:
                    ImGui.Text($"{f.Name}");
                    ImGui.SameLine();
                    ImGui.TextDisabled($"  {f.ProjectRelativePath}");
                    break;
                default:
                    ImGui.Text(e.Name);
                    break;
            }
        }
        ImGui.TreePop();
    }

    private static void DrawMaterialRow(MaterialAssetEntry m, AssetLibrary library, Action<AppUiMsg> dispatchApp)
    {
        ImGui.PushID(m.Guid.ToString("N"));

        var open = ImGui.TreeNodeEx(m.Name, ImGuiTreeNodeFlags.SpanAvailWidth);
        ImGui.SameLine();
        var texLabel = m.AlbedoTex is null ? "no tex" : "tex";
        ImGui.TextDisabled($"  [{texLabel}]  {m.ProjectRelativePath}");

        if (open)
        {
            DrawMaterialEditor(m, library, dispatchApp);
            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private static void DrawMaterialEditor(MaterialAssetEntry m, AssetLibrary library, Action<AppUiMsg> dispatchApp)
    {
        var p = m.Params;
        var albedo = new Vector3(
            p.Albedo.Length > 0 ? p.Albedo[0] : 0f,
            p.Albedo.Length > 1 ? p.Albedo[1] : 0f,
            p.Albedo.Length > 2 ? p.Albedo[2] : 0f);

        // Dispatch on every change (no deactivated-after-edit gating) so a
        // drag previews live in the renderer. Each dispatch rewrites the
        // .mat.json; at ~60 fps this is cheap for a few-KB file.
        var nextAlbedo = DebugFields.ColorEdit("Albedo", albedo);
        bool albedoChanged = nextAlbedo != albedo;

        var nextSpec = DebugFields.DragFloat("Spec Strength", p.SpecularStrength, 0.005f, 0f, 1f);
        bool specChanged = !FloatsEqual(nextSpec, p.SpecularStrength);

        var nextShininess = DebugFields.DragFloat("Shininess", p.Shininess, 1f, 1f, MaterialParams.ShininessRange);
        bool shinChanged = !FloatsEqual(nextShininess, p.Shininess);

        // Texture combo: standalone Texture entries in the library, plus a
        // "(none)" sentinel. Sub-asset textures imported from glTF files are
        // not enumerated here (they live as sub-assets of their mesh file);
        // assign those via the Scene panel.
        var textures = library.EntriesOfKind(AssetKind.Texture)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var items = new string[textures.Length + 1];
        items[0] = "(none)";
        for (int i = 0; i < textures.Length; i++) items[i + 1] = textures[i].Name;

        int selected = 0;
        if (m.AlbedoTex is AssetRef cur && string.IsNullOrEmpty(cur.Sub))
        {
            for (int i = 0; i < textures.Length; i++)
                if (textures[i].Guid == cur.Guid) { selected = i + 1; break; }
        }
        int nextSelected = DebugFields.ComboEdit("Albedo Tex", selected, items);
        bool texChanged = nextSelected != selected;

        AssetRef? nextTex = m.AlbedoTex;
        if (texChanged)
            nextTex = nextSelected == 0 ? null : new AssetRef(textures[nextSelected - 1].Guid);

        if (albedoChanged || specChanged || shinChanged || texChanged)
        {
            dispatchApp(new AppUiMsg.RequestUpdateMaterial(
                Guid: m.Guid,
                Albedo: new[] { nextAlbedo.X, nextAlbedo.Y, nextAlbedo.Z },
                SpecularStrength: nextSpec,
                Shininess: nextShininess,
                AlbedoTexGuid: nextTex?.Guid,
                AlbedoTexSub: nextTex?.Sub));
        }
    }

    private static bool FloatsEqual(float a, float b) => MathF.Abs(a - b) <= 1e-6f;
}
