using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;

namespace RenderLab.Debug;

/// <summary>
/// Two-way debug panel for the demo sphere — its world placement
/// (<see cref="Transform"/>) and its Blinn-Phong <see cref="MaterialParams"/>
/// grouped into a single window so the whole object can be tweaked in one place.
/// </summary>
public static class SphereDebugMenu
{
    public static (Transform transform, MaterialParams material) Draw(
        Transform transform, MaterialParams material)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 650), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(320, 240), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Sphere"))
        {
            ImGui.End();
            return (transform, material);
        }

        ImGui.SeparatorText("Transform");
        var position = DebugFields.DragVector3("Position", transform.Position, 0.05f);
        var scale = DebugFields.SliderFloat("Scale", transform.Scale, 0.1f, 5f);

        ImGui.SeparatorText("Material");
        var albedo = DebugFields.ColorEdit("Albedo", material.Albedo);
        var specStrength = DebugFields.SliderFloat("Spec Strength", material.SpecularStrength, 0f, 1f);
        var shininess = DebugFields.SliderFloat("Shininess", material.Shininess, 1f, MaterialParams.ShininessRange,
            flags: ImGuiSliderFlags.Logarithmic);

        ImGui.End();

        return (
            transform with { Position = position, Scale = scale },
            new MaterialParams(albedo, specStrength, shininess)
        );
    }
}
