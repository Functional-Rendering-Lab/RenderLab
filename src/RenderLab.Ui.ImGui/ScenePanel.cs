using System.Numerics;
using ImGuiNET;
using RenderLab.Assets;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Scene outliner: read-only camera summary, a clickable drawable list, a
/// clickable lights list, an Environment entry. Edits live in the Inspector —
/// clicking an item here emits <see cref="UiMsg.Select"/>. Add / remove /
/// clone stay here because they're list operations, not item edits.
/// </summary>
public static class ScenePanel
{
    public static void Draw(UiModel model, IAssetCatalog catalog,
        Action<UiMsg> dispatch, Action<AppUiMsg> dispatchApp)
    {
        ImGui.SetNextWindowPos(new Vector2(640, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(360, 460), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Scene"))
        {
            ImGui.End();
            return;
        }

        // Camera + Environment surface global view / lighting state through
        // the same Select → Inspector pipe as scene items. Rendered as Leaf
        // tree nodes so they align with the Drawables / Lights headers
        // (same arrow-gutter indent, no children).
        LeafEntry("Camera", model.Selection is Selection.Camera,
            () => dispatch(new UiMsg.Select(new Selection.Camera())));
        LeafEntry("Environment", model.Selection is Selection.Environment,
            () => dispatch(new UiMsg.Select(new Selection.Environment())));

        if (ImGui.TreeNodeEx($"Drawables ({model.Drawables.Length})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            AcceptMeshDrop(dispatchApp);
            DrawDrawableList(model, catalog, dispatch);
            ImGui.TreePop();
        }
        else
        {
            // Drop on the collapsed header still adds a drawable — saves
            // the user from expanding to use the feature.
            AcceptMeshDrop(dispatchApp);
        }

        if (ImGui.TreeNodeEx($"Lights ({model.Lights.Length})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < model.Lights.Length; i++)
            {
                var selected = model.Selection is Selection.Light s && s.Index == i;
                var label = model.Lights[i] switch
                {
                    PointLight p       => $"[{i}] point   pos=({p.Position.X:F2},{p.Position.Y:F2},{p.Position.Z:F2})  i={p.Intensity.Value:F2}##l{i}",
                    DirectionalLight d => $"[{i}] dirlight dir=({d.Direction.Value.X:F2},{d.Direction.Value.Y:F2},{d.Direction.Value.Z:F2})  i={d.Intensity.Value:F2}##l{i}",
                    _                  => $"[{i}] light##l{i}",
                };
                if (ImGui.Selectable(label, selected))
                    dispatch(new UiMsg.Select(new Selection.Light(i)));
            }
            if (ImGui.Button("+ Point"))
                dispatch(new UiMsg.AddPointLight());
            ImGui.SameLine();
            if (ImGui.Button("+ Directional"))
                dispatch(new UiMsg.AddDirectionalLight());
            ImGui.TreePop();
        }

        ImGui.End();
    }

    private static unsafe void AcceptMeshDrop(Action<AppUiMsg> dispatchApp)
    {
        if (!ImGui.BeginDragDropTarget()) return;
        var payload = ImGui.AcceptDragDropPayload(AssetBrowserPanel.MeshDragPayloadType);
        if (payload.NativePtr != null && payload.IsDelivery() && payload.DataSize == 16)
        {
            var span = new ReadOnlySpan<byte>((void*)payload.Data, 16);
            var guid = new Guid(span);
            dispatchApp(new AppUiMsg.RequestAddDrawableFromAsset(guid));
        }
        ImGui.EndDragDropTarget();
    }

    private static void LeafEntry(string label, bool selected, Action onClick)
    {
        var flags = ImGuiTreeNodeFlags.Leaf
                  | ImGuiTreeNodeFlags.NoTreePushOnOpen
                  | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;
        ImGui.TreeNodeEx(label, flags);
        if (ImGui.IsItemClicked()) onClick();
    }

    private static void DrawDrawableList(UiModel model, IAssetCatalog catalog, Action<UiMsg> dispatch)
    {
        Guid? selectedDrawable = model.Selection is Selection.Drawable d ? d.LocalId : null;

        for (int i = 0; i < model.Drawables.Length; i++)
        {
            var dr = model.Drawables[i];
            var selected = selectedDrawable == dr.LocalId;
            var p = dr.Transform.Position;
            var label = $"{dr.Name}  pos=({p.X:F2},{p.Y:F2},{p.Z:F2})##{dr.LocalId}";
            if (ImGui.Selectable(label, selected))
                dispatch(new UiMsg.Select(new Selection.Drawable(dr.LocalId)));
        }

        var hasSelection = selectedDrawable is Guid sel
            && model.Drawables.Any(d => d.LocalId == sel);

        if (!hasSelection) ImGui.BeginDisabled();
        if (ImGui.Button("+ clone") && selectedDrawable is Guid cloneId)
        {
            var src = model.Drawables.First(d => d.LocalId == cloneId);
            var nudged = src.Transform with { Position = src.Transform.Position + new Vector3(1f, 0f, 0f) };
            dispatch(new UiMsg.AddDrawable($"{src.Name} copy", src.Mesh, nudged, src.Material));
        }
        ImGui.SameLine();
        if (ImGui.Button("- remove") && selectedDrawable is Guid removeId)
            dispatch(new UiMsg.RemoveDrawable(removeId));
        if (!hasSelection) ImGui.EndDisabled();

        DrawAddFromAssets(catalog, dispatch);
    }

    // "+ Add" picker — composes a new drawable from any registered mesh +
    // material. Selections persist across frames via static slots.
    private static MeshId _addMesh = MeshId.None;
    private static MaterialId _addMaterial = MaterialId.None;

    private static void DrawAddFromAssets(IAssetCatalog catalog, Action<UiMsg> dispatch)
    {
        var meshes = catalog.AllMeshes.ToArray();
        var materials = catalog.AllMaterials.ToArray();
        if (meshes.Length == 0 || materials.Length == 0) return;

        if (_addMesh.IsNone) _addMesh = meshes[0].Id;
        if (_addMaterial.IsNone) _addMaterial = materials[0].Id;

        ImGui.SeparatorText("Add from registry");
        InspectorPanel.MeshCombo("Mesh##add", meshes, ref _addMesh);
        InspectorPanel.MaterialCombo("Material##add", materials, ref _addMaterial);
        if (ImGui.Button("+ Add"))
        {
            var meshName = meshes.First(m => m.Id == _addMesh).Name;
            dispatch(new UiMsg.AddDrawable(meshName, _addMesh, Transform.Default, _addMaterial));
        }
    }
}
