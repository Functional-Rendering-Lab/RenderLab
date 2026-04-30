using System.Collections.Immutable;
using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

/// <summary>
/// View fragment for the lighting panel: shading mode, lighting-only toggle,
/// hemispheric ambient, background clear color, and a per-light editor over
/// the scene's light array (point + directional). Emits one <see cref="UiMsg"/>
/// per independent concern so the reducer can update them in isolation; light
/// edits carry the index so the SSBO and the model stay in sync.
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
        ImmutableArray<Light> lights, HemisphericAmbient ambient,
        ShadingMode mode, bool lightingOnly,
        Vector3 clearColor, Action<UiMsg> dispatch)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 440), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(360, 480), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Lighting"))
        {
            ImGui.End();
            return;
        }

        ImGui.SeparatorText("Shading");
        var newMode = (ShadingMode)DebugFields.ComboEdit("Model", (int)mode, ShadingModeNames);
        var newLightingOnly = DebugFields.Checkbox("Lighting only (no albedo)", lightingOnly);

        ImGui.SeparatorText("Ambient (hemispheric)");
        var newSky    = DebugFields.ColorEdit("Sky",    ambient.Sky);
        var newGround = DebugFields.ColorEdit("Ground", ambient.Ground);

        ImGui.SeparatorText("Background");
        var newClearColor = DebugFields.ColorEdit("Clear color", clearColor);

        ImGui.SeparatorText($"Lights ({lights.Length})");
        if (ImGui.Button("+ Point"))
            dispatch(new UiMsg.AddPointLight());
        ImGui.SameLine();
        if (ImGui.Button("+ Directional"))
            dispatch(new UiMsg.AddDirectionalLight());

        for (int i = 0; i < lights.Length; i++)
            DrawLightRow(i, lights[i], dispatch);

        ImGui.End();

        if (newMode != mode)
            dispatch(new UiMsg.SetShading(newMode));

        if (newLightingOnly != lightingOnly)
            dispatch(new UiMsg.SetLightingOnly(newLightingOnly));

        if (newSky != ambient.Sky || newGround != ambient.Ground)
            dispatch(new UiMsg.UpdateAmbient(new HemisphericAmbient(newSky, newGround)));

        if (newClearColor != clearColor)
            dispatch(new UiMsg.SetClearColor(newClearColor));
    }

    private static void DrawLightRow(int index, Light light, Action<UiMsg> dispatch)
    {
        ImGui.PushID(index);

        var color = light switch
        {
            PointLight p => p.Color,
            DirectionalLight d => d.Color,
            _ => Vector3.One,
        };

        // Use a colored swatch in the header so users can pick a light at a glance.
        var swatch = new Vector4(color.X, color.Y, color.Z, 1f);
        ImGui.PushStyleColor(ImGuiCol.Header, swatch * 0.4f);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, swatch * 0.6f);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, swatch * 0.7f);

        var label = light switch
        {
            PointLight       => $"Light {index} (point)",
            DirectionalLight => $"Light {index} (directional)",
            _                => $"Light {index}",
        };
        bool open = ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(3);

        if (open)
        {
            switch (light)
            {
                case PointLight p:       DrawPointBody(index, p, dispatch); break;
                case DirectionalLight d: DrawDirectionalBody(index, d, dispatch); break;
            }

            if (ImGui.Button("Remove"))
                dispatch(new UiMsg.RemoveLight(index));
        }

        ImGui.PopID();
    }

    private static void DrawPointBody(int index, PointLight light, Action<UiMsg> dispatch)
    {
        var position  = DebugFields.DragVector3("Position", light.Position, 0.05f);
        var color     = DebugFields.ColorEdit("Color", light.Color);
        var intensity = DebugFields.DragFloat("Intensity", light.Intensity.Value, 0.05f, 0f, 100f);

        var next = new PointLight(position, color, Intensity.Of(MathF.Max(intensity, 0f)));
        if (!next.Equals(light))
            dispatch(new UiMsg.UpdateLight(index, next));
    }

    private static void DrawDirectionalBody(int index, DirectionalLight light, Action<UiMsg> dispatch)
    {
        // Edit the raw direction; re-normalize via Direction.Create on dispatch.
        // Reject zero-vector edits silently — keep the previous valid direction.
        var rawDir    = DebugFields.DragVector3("Direction", light.Direction.Value, 0.02f);
        var color     = DebugFields.ColorEdit("Color", light.Color);
        var intensity = DebugFields.DragFloat("Intensity", light.Intensity.Value, 0.05f, 0f, 100f);

        Direction nextDir;
        try { nextDir = Direction.Create(rawDir); }
        catch (ArgumentException) { nextDir = light.Direction; }

        var next = new DirectionalLight(nextDir, color, Intensity.Of(MathF.Max(intensity, 0f)));
        if (!next.Equals(light))
            dispatch(new UiMsg.UpdateLight(index, next));
    }
}
