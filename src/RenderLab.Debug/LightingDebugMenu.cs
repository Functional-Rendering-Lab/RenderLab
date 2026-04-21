using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;

namespace RenderLab.Debug;

/// <summary>
/// Two-way debug panel for <see cref="PointLight"/> and the deferred shading
/// mode. Material parameters live in <see cref="SphereDebugMenu"/>. Call
/// <see cref="Draw"/> each frame inside an ImGui context — it returns the
/// (potentially edited) values.
/// </summary>
public static class LightingDebugMenu
{
    private static readonly string[] ShadingModeNames =
    {
        "Lambertian (diffuse only)",
        "Phong (R·V)",
        "Blinn-Phong (N·H)",
    };

    public static (PointLight light, ShadingMode mode, bool lightingOnly) Draw(
        PointLight light, ShadingMode mode, bool lightingOnly)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(320, 220), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Lighting"))
        {
            ImGui.End();
            return (light, mode, lightingOnly);
        }

        ImGui.SeparatorText("Shading");
        int modeIndex = (int)mode;
        ImGui.Combo("Model", ref modeIndex, ShadingModeNames, ShadingModeNames.Length);
        var newMode = (ShadingMode)modeIndex;

        var newLightingOnly = DebugFields.Checkbox("Lighting only (no albedo)", lightingOnly);

        ImGui.SeparatorText("Light");
        var position = DebugFields.DragVector3("Position", light.Position, 0.05f);
        var color = DebugFields.ColorEdit("Color", light.Color);
        var intensity = DebugFields.DragFloat("Intensity", light.Intensity, 0.05f, 0f, 100f);

        ImGui.End();

        return (
            light with { Position = position, Color = color, Intensity = intensity },
            newMode,
            newLightingOnly
        );
    }
}
