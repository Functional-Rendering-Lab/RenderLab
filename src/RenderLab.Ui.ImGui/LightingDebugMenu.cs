using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// View fragment for the lighting panel (shading mode, light, lighting-only toggle).
/// Emits <see cref="UiMsg.UpdateLight"/>, <see cref="UiMsg.SetShading"/> and
/// <see cref="UiMsg.SetLightingOnly"/> on change — one message per independent
/// concern so the reducer can update them in isolation.
/// </summary>
public static class LightingDebugMenu
{
    private static readonly string[] ShadingModeNames =
    {
        "Lambertian (diffuse only)",
        "Phong (R·V)",
        "Blinn-Phong (N·H)",
    };

    public static void Draw(
        PointLight light, ShadingMode mode, bool lightingOnly,
        Action<UiMsg> dispatch)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(320, 220), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Lighting"))
        {
            ImGui.End();
            return;
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

        var nextLight = light with { Position = position, Color = color, Intensity = intensity };
        if (!nextLight.Equals(light))
            dispatch(new UiMsg.UpdateLight(nextLight));

        if (newMode != mode)
            dispatch(new UiMsg.SetShading(newMode));

        if (newLightingOnly != lightingOnly)
            dispatch(new UiMsg.SetLightingOnly(newLightingOnly));
    }
}
