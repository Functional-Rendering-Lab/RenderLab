using System.Numerics;
using ImGuiNET;
using RenderLab.Assets;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Read-only inventory of every registered asset, grouped by kind, with the
/// number of drawables (or other materials, for textures) that reference each
/// id. Remove buttons land in Step I.
/// </summary>
public static class AssetBrowserPanel
{
    public static void Draw(UiModel model, IAssetCatalog catalog, Action<UiMsg> dispatch)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(360, 360), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Asset Browser"))
        {
            ImGui.End();
            return;
        }

        DrawMeshes(model, catalog);
        DrawMaterials(model, catalog);
        DrawTextures(catalog);

        ImGui.End();
    }

    private static void DrawMeshes(UiModel model, IAssetCatalog catalog)
    {
        var meshes = catalog.AllMeshes.ToArray();
        if (!ImGui.TreeNodeEx($"Meshes ({meshes.Length})", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (var m in meshes)
        {
            int refs = model.Drawables.Count(d => d.Mesh == m.Id);
            ImGui.BulletText($"#{m.Id.Value}  {m.Name}  ({m.Data.Vertices.Length}v / {m.Data.Indices.Length / 3}t)  refs={refs}");
        }
        ImGui.TreePop();
    }

    private static void DrawMaterials(UiModel model, IAssetCatalog catalog)
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
            ImGui.BulletText($"#{m.Id.Value}  {m.Name}  [{kind}]  refs={refs}");
        }
        ImGui.TreePop();
    }

    private static void DrawTextures(IAssetCatalog catalog)
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
            ImGui.BulletText($"#{t.Id.Value}  {t.Name}  ({t.Width}×{t.Height} {t.Format})  refs={refs}");
        }
        ImGui.TreePop();
    }
}
