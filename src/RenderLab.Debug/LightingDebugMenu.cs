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
    public static PointLight Draw(PointLight light)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 180), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Lighting"))
        {
            ImGui.End();
            return light;
        }

        ImGui.Text("Deferred Blinn-Phong");
        ImGui.Separator();

        var position = DebugFields.DragVector3("Position", light.Position, 0.05f);
        var color = DebugFields.ColorEdit("Color", light.Color);
        var intensity = DebugFields.DragFloat("Intensity", light.Intensity, 0.05f, 0f, 100f);

        ImGui.End();

        return light with
        {
            Position = position,
            Color = color,
            Intensity = intensity,
        };
    }
}
