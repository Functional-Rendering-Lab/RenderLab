using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;

namespace RenderLab.Debug;

/// <summary>
/// Two-way debug panel for <see cref="PointLight"/>.
/// Call <see cref="Draw"/> each frame inside an ImGui context — it returns the (potentially edited) light.
/// </summary>
public static class LightingDebugMenu
{
    public static (PointLight light, MaterialParams material) Draw(PointLight light, MaterialParams material)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 260), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Lighting"))
        {
            ImGui.End();
            return (light, material);
        }

        ImGui.Text("Deferred Blinn-Phong");
        ImGui.Separator();

        var position = DebugFields.DragVector3("Position", light.Position, 0.05f);
        var color = DebugFields.ColorEdit("Color", light.Color);
        var intensity = DebugFields.DragFloat("Intensity", light.Intensity, 0.05f, 0f, 100f);

        ImGui.Separator();
        ImGui.Text("Material");

        var specStrength = DebugFields.SliderFloat("Spec Strength", material.SpecularStrength, 0f, 1f);
        var shininess = DebugFields.SliderFloat("Shininess", material.Shininess, 1f, MaterialParams.ShininessRange,
            flags: ImGuiSliderFlags.Logarithmic);

        ImGui.End();

        return (
            light with { Position = position, Color = color, Intensity = intensity },
            new MaterialParams(specStrength, shininess)
        );
    }
}
