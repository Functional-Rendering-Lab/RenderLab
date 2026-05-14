using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// Render-mode toggles for the shading model: which BRDF to use and whether
/// to drop albedo from the result. Light values, ambient, clear color, and
/// background mode are edited in the Inspector (select a light or the
/// Environment entry in the Scene panel).
/// </summary>
public static class LightingDebugMenu
{
    private static readonly string[] ShadingModeNames =
    {
        "Lambertian (diffuse only)",
        "Phong (R·V)",
        "Blinn-Phong (N·H)",
    };

    public static void Draw(ShadingMode mode, bool lightingOnly, Action<UiMsg> dispatch)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(320, 110), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Lighting"))
        {
            ImGui.End();
            return;
        }

        var newMode = (ShadingMode)DebugFields.ComboEdit("Model", (int)mode, ShadingModeNames);
        var newLightingOnly = DebugFields.Checkbox("Lighting only (no albedo)", lightingOnly);

        ImGui.End();

        if (newMode != mode)
            dispatch(new UiMsg.SetShading(newMode));
        if (newLightingOnly != lightingOnly)
            dispatch(new UiMsg.SetLightingOnly(newLightingOnly));
    }
}
