using System.Numerics;
using ImGuiNET;
using RenderLab.Assets;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;
using Scene = RenderLab.Scene.Scene;

/// <summary>
/// Scene editor panel: read-only camera and lights, editable drawable list with
/// selection, add/remove, and an inline transform + material inspector for the
/// selected drawable. Add clones the current selection (a real "from registry"
/// flow lands with the asset browser in Step E).
/// </summary>
public static class ScenePanel
{
    public static void Draw(UiModel model, Scene scene, IAssetCatalog catalog, Action<UiMsg> dispatch)
    {
        ImGui.SetNextWindowPos(new Vector2(640, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(360, 460), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Scene"))
        {
            ImGui.End();
            return;
        }

        if (ImGui.TreeNodeEx("Camera", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var c = scene.Camera;
            ImGui.BulletText($"pos    ({c.Position.X:F2}, {c.Position.Y:F2}, {c.Position.Z:F2})");
            ImGui.BulletText($"target ({c.Target.X:F2}, {c.Target.Y:F2}, {c.Target.Z:F2})");
            ImGui.BulletText($"fov    {c.FovRadians * 180f / MathF.PI:F1}°");
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx($"Drawables ({model.Drawables.Length})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawDrawableList(model, dispatch);
            DrawSelectedInspector(model, catalog, dispatch);
            ImGui.TreePop();
        }

        if (ImGui.TreeNodeEx($"Lights ({scene.Lights.Length})", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < scene.Lights.Length; i++)
            {
                switch (scene.Lights[i])
                {
                    case PointLight p:
                        ImGui.BulletText($"[{i}] point   pos=({p.Position.X:F2},{p.Position.Y:F2},{p.Position.Z:F2})  intensity={p.Intensity.Value:F2}");
                        break;
                    case DirectionalLight d:
                        var dv = d.Direction.Value;
                        ImGui.BulletText($"[{i}] dirlight dir=({dv.X:F2},{dv.Y:F2},{dv.Z:F2})  intensity={d.Intensity.Value:F2}");
                        break;
                }
            }
            ImGui.TreePop();
        }

        ImGui.End();
    }

    private static void DrawDrawableList(UiModel model, Action<UiMsg> dispatch)
    {
        for (int i = 0; i < model.Drawables.Length; i++)
        {
            var d = model.Drawables[i];
            var selected = model.SelectedDrawable == d.LocalId;
            var p = d.Transform.Position;
            var label = $"{d.Name}  pos=({p.X:F2},{p.Y:F2},{p.Z:F2})##{d.LocalId}";
            if (ImGui.Selectable(label, selected))
                dispatch(new UiMsg.SelectDrawable(d.LocalId));
        }

        var hasSelection = model.SelectedDrawable is Guid sel
            && model.Drawables.Any(d => d.LocalId == sel);

        if (!hasSelection) ImGui.BeginDisabled();
        if (ImGui.Button("+ clone"))
        {
            var src = model.Drawables.First(d => d.LocalId == model.SelectedDrawable);
            var nudged = src.Transform with { Position = src.Transform.Position + new Vector3(1f, 0f, 0f) };
            // Cloned drawable shares the source material id — editing it edits both.
            // A "duplicate material" affordance lands with the asset browser.
            dispatch(new UiMsg.AddDrawable($"{src.Name} copy", src.Mesh, nudged, src.Material));
        }
        ImGui.SameLine();
        if (ImGui.Button("- remove") && model.SelectedDrawable is Guid removeId)
            dispatch(new UiMsg.RemoveDrawable(removeId));
        if (!hasSelection) ImGui.EndDisabled();
    }

    private static void DrawSelectedInspector(UiModel model, IAssetCatalog catalog, Action<UiMsg> dispatch)
    {
        if (model.SelectedDrawable is not Guid id) return;
        var drawable = model.Drawables.FirstOrDefault(d => d.LocalId == id);
        if (drawable is null) return;

        ImGui.SeparatorText($"Selected: {drawable.Name}");

        var t = drawable.Transform;
        var position = DebugFields.DragVector3("Position", t.Position, 0.05f);
        var eulerCurrent = QuatToEulerDeg(t.Rotation);
        var eulerNext = DebugFields.DragVector3("Rotation (deg)", eulerCurrent, 1f);
        var scale = DebugFields.DragFloat("Scale", t.Scale, 0.02f, 0.1f, 5f);

        var rotation = eulerNext == eulerCurrent ? t.Rotation : EulerDegToQuat(eulerNext);
        var nextTransform = t with { Position = position, Rotation = rotation, Scale = scale };
        if (!nextTransform.Equals(t))
            dispatch(new UiMsg.SetDrawableTransform(id, nextTransform));

        // Material edits target the registered asset by id. The reducer is a
        // no-op on UpdateMaterialAsset; the shell applies it to the registry
        // before re-resolving for the next frame.
        if (catalog.GetMaterial(drawable.Material) is BlinnPhongMaterial bp)
        {
            var albedo = DebugFields.ColorEdit("Albedo", bp.Albedo);
            var spec = DebugFields.DragFloat("Spec Strength", bp.SpecularStrength, 0.005f, 0f, 1f);
            var shininess = DebugFields.DragFloat("Shininess", bp.Shininess, 1f, 1f, MaterialParams.ShininessRange);
            var next = bp with { Albedo = albedo, SpecularStrength = spec, Shininess = shininess };
            if (!next.Equals(bp))
                dispatch(new UiMsg.UpdateMaterialAsset(bp.Id, next));
        }
    }

    // Quaternion ↔ Euler conversion for the inspector. Tait-Bryan ZYX
    // intrinsic (roll-pitch-yaw) — the same convention
    // <see cref="Quaternion.CreateFromYawPitchRoll"/> produces, so a
    // round-trip is stable away from gimbal lock.
    private static Vector3 QuatToEulerDeg(Quaternion q)
    {
        float sinrCosp = 2f * (q.W * q.X + q.Y * q.Z);
        float cosrCosp = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        float roll = MathF.Atan2(sinrCosp, cosrCosp);

        float sinp = 2f * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinp) >= 1f ? MathF.CopySign(MathF.PI / 2f, sinp) : MathF.Asin(sinp);

        float sinyCosp = 2f * (q.W * q.Z + q.X * q.Y);
        float cosyCosp = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
        float yaw = MathF.Atan2(sinyCosp, cosyCosp);

        return new Vector3(pitch, yaw, roll) * (180f / MathF.PI);
    }

    private static Quaternion EulerDegToQuat(Vector3 eulerDeg)
    {
        var r = eulerDeg * (MathF.PI / 180f);
        return Quaternion.CreateFromYawPitchRoll(r.Y, r.X, r.Z);
    }
}
