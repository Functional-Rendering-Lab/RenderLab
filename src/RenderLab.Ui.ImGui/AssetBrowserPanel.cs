using System.Numerics;
using ImGuiNET;
using RenderLab.Assets;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Inventory of every registered asset, grouped by kind, with reference counts
/// and per-row remove buttons. Buttons are disabled when the asset is built-in
/// or still referenced; the shell does the actual removal via a registry call
/// so this panel stays pure-message.
/// </summary>
public static class AssetBrowserPanel
{
    public static void Draw(UiModel model, IAssetCatalog catalog, Action<AppUiMsg> dispatchApp)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(380, 380), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Asset Browser"))
        {
            ImGui.End();
            return;
        }

        DrawMeshes(model, catalog, dispatchApp);
        DrawMaterials(model, catalog, dispatchApp);
        DrawTextures(catalog, dispatchApp);

        ImGui.End();
    }

    private static void DrawMeshes(UiModel model, IAssetCatalog catalog, Action<AppUiMsg> dispatchApp)
    {
        var meshes = catalog.AllMeshes.ToArray();
        if (!ImGui.TreeNodeEx($"Meshes ({meshes.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (var m in meshes)
        {
            int refs = model.Drawables.Count(d => d.Mesh == m.Id);
            ImGui.Text($"#{m.Id.Value}  {m.Name}  ({m.Data.Vertices.Length}v / {m.Data.Indices.Length / 3}t)  refs={refs}");
            ImGui.SameLine();
            RemoveButton($"##rm-mesh-{m.Id.Value}",
                disabledReason: catalog.IsBuiltin(m.Id) ? "built-in" : refs > 0 ? $"in use by {refs} drawable(s)" : null,
                onClick: () => dispatchApp(new AppUiMsg.RequestRemoveMesh(m.Id)));
        }
        ImGui.TreePop();
    }

    private static void DrawMaterials(UiModel model, IAssetCatalog catalog, Action<AppUiMsg> dispatchApp)
    {
        var mats = catalog.AllMaterials.ToArray();
        if (!ImGui.TreeNodeEx($"Materials ({mats.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (var m in mats)
        {
            int refs = model.Drawables.Count(d => d.Material == m.Id);
            var kind = m switch
            {
                BlinnPhongMaterial bp => bp.AlbedoMap.IsNone ? "BlinnPhong" : $"BlinnPhong+tex#{bp.AlbedoMap.Value}",
                _ => m.GetType().Name,
            };
            ImGui.Text($"#{m.Id.Value}  {m.Name}  [{kind}]  refs={refs}");
            ImGui.SameLine();
            RemoveButton($"##rm-mat-{m.Id.Value}",
                disabledReason: catalog.IsBuiltin(m.Id) ? "built-in" : refs > 0 ? $"in use by {refs} drawable(s)" : null,
                onClick: () => dispatchApp(new AppUiMsg.RequestRemoveMaterial(m.Id)));
        }
        ImGui.TreePop();
    }

    private static void DrawTextures(IAssetCatalog catalog, Action<AppUiMsg> dispatchApp)
    {
        var texs = catalog.AllTextures.ToArray();
        if (!ImGui.TreeNodeEx($"Textures ({texs.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        // Texture refcount = number of materials whose albedo map points at it.
        // Cheap to recompute each frame at this scale; revisit if the registry
        // grows past hundreds of assets.
        var refsByTex = new Dictionary<int, int>();
        foreach (var mat in catalog.AllMaterials)
            if (mat is BlinnPhongMaterial bp && !bp.AlbedoMap.IsNone)
                refsByTex[bp.AlbedoMap.Value] = refsByTex.GetValueOrDefault(bp.AlbedoMap.Value) + 1;

        foreach (var t in texs)
        {
            int refs = refsByTex.GetValueOrDefault(t.Id.Value);
            ImGui.Text($"#{t.Id.Value}  {t.Name}  ({t.Width}×{t.Height} {t.Format})  refs={refs}");
            ImGui.SameLine();
            RemoveButton($"##rm-tex-{t.Id.Value}",
                disabledReason: catalog.IsBuiltin(t.Id) ? "built-in" : refs > 0 ? $"in use by {refs} material(s)" : null,
                onClick: () => dispatchApp(new AppUiMsg.RequestRemoveTexture(t.Id)));
        }
        ImGui.TreePop();
    }

    private static void RemoveButton(string id, string? disabledReason, Action onClick)
    {
        var disabled = disabledReason is not null;
        if (disabled) ImGui.BeginDisabled();
        if (ImGui.SmallButton("remove" + id)) onClick();
        if (disabled) ImGui.EndDisabled();
        if (disabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(disabledReason);
    }
}
