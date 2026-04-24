using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// View fragment for the demo sphere — placement (<see cref="Transform"/>) and
/// Blinn-Phong <see cref="MaterialParams"/> in one panel. Dispatches separate
/// messages for transform and material so the reducer can handle each concern
/// on its own.
/// </summary>
public static class SphereDebugMenu
{
    public static void Draw(
        Transform transform, MaterialParams material,
        Action<UiMsg> dispatch)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 650), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(320, 240), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Sphere"))
        {
            ImGui.End();
            return;
        }

        ImGui.SeparatorText("Transform");
        var position = DebugFields.DragVector3("Position", transform.Position, 0.05f);
        var scale = DebugFields.DragFloat("Scale", transform.Scale, 0.02f, 0.1f, 5f);

        ImGui.SeparatorText("Material");
        var albedo = DebugFields.ColorEdit("Albedo", material.Albedo);
        var specStrength = DebugFields.DragFloat("Spec Strength", material.SpecularStrength, 0.005f, 0f, 1f);
        var shininess = DebugFields.DragFloat("Shininess", material.Shininess, 1f, 1f, MaterialParams.ShininessRange);

        ImGui.End();

        var nextTransform = transform with { Position = position, Scale = scale };
        if (!nextTransform.Equals(transform))
            dispatch(new UiMsg.UpdateMeshTransform(nextTransform));

        var nextMaterial = new MaterialParams(albedo, specStrength, shininess);
        if (!nextMaterial.Equals(material))
            dispatch(new UiMsg.UpdateMaterial(nextMaterial));
    }
}
