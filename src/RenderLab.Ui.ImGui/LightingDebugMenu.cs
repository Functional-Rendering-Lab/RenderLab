using System.Collections.Immutable;
using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// View fragment for the lighting panel: shading mode, lighting-only toggle,
/// background clear color, and a per-light editor over the scene's point-light
/// array. Emits one <see cref="UiMsg"/> per independent concern so the reducer
/// can update them in isolation; light edits carry the index so the SSBO and
/// the model stay in sync.
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
        ImmutableArray<PointLight> lights, ShadingMode mode, bool lightingOnly,
        Vector3 clearColor, Action<UiMsg> dispatch)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(340, 420), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Lighting"))
        {
            ImGui.End();
            return;
        }

        ImGui.SeparatorText("Shading");
        var newMode = (ShadingMode)DebugFields.ComboEdit("Model", (int)mode, ShadingModeNames);
        var newLightingOnly = DebugFields.Checkbox("Lighting only (no albedo)", lightingOnly);

        ImGui.SeparatorText("Background");
        var newClearColor = DebugFields.ColorEdit("Clear color", clearColor);

        ImGui.SeparatorText($"Lights ({lights.Length})");
        if (ImGui.Button("+ Add light"))
            dispatch(new UiMsg.AddLight());

        for (int i = 0; i < lights.Length; i++)
            DrawLightRow(i, lights[i], dispatch);

        ImGui.End();

        if (newMode != mode)
            dispatch(new UiMsg.SetShading(newMode));

        if (newLightingOnly != lightingOnly)
            dispatch(new UiMsg.SetLightingOnly(newLightingOnly));

        if (newClearColor != clearColor)
            dispatch(new UiMsg.SetClearColor(newClearColor));
    }

    private static void DrawLightRow(int index, PointLight light, Action<UiMsg> dispatch)
    {
        ImGui.PushID(index);

        // Use a colored swatch in the header so users can pick a light at a glance.
        var swatch = new Vector4(light.Color.X, light.Color.Y, light.Color.Z, 1f);
        ImGui.PushStyleColor(ImGuiCol.Header, swatch * 0.4f);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, swatch * 0.6f);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, swatch * 0.7f);

        bool open = ImGui.CollapsingHeader($"Light {index}", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(3);

        if (open)
        {
            var position  = DebugFields.DragVector3("Position", light.Position, 0.05f);
            var color     = DebugFields.ColorEdit("Color", light.Color);
            var intensity = DebugFields.DragFloat("Intensity", light.Intensity, 0.05f, 0f, 100f);

            var next = light with { Position = position, Color = color, Intensity = intensity };
            if (!next.Equals(light))
                dispatch(new UiMsg.UpdateLight(index, next));

            if (ImGui.Button("Remove"))
                dispatch(new UiMsg.RemoveLight(index));
        }

        ImGui.PopID();
    }
}
