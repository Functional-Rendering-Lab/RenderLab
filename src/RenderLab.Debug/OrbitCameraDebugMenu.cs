using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;

namespace RenderLab.Debug;

/// <summary>
/// Two-way debug panel for <see cref="OrbitState"/>.
/// Call <see cref="Draw"/> each frame inside an ImGui context — it returns the (potentially edited) state.
/// </summary>
public static class OrbitCameraDebugMenu
{
    private const float DegPerRad = 180f / MathF.PI;
    private const float RadPerDeg = MathF.PI / 180f;

    public static OrbitState Draw(OrbitState state)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 160), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 200), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Camera"))
        {
            ImGui.End();
            return state;
        }

        var distance = DebugFields.DragFloat("Distance", state.Distance, 0.05f, 0.3f, 50f);

        // Edit in degrees, store in radians
        float yawDeg = state.Yaw * DegPerRad;
        float pitchDeg = state.Pitch * DegPerRad;
        yawDeg = DebugFields.DragFloat("Yaw", yawDeg, 0.5f, format: "%.1f deg");
        pitchDeg = DebugFields.DragFloat("Pitch", pitchDeg, 0.5f, -89.9f, 89.9f, "%.1f deg");

        var target = DebugFields.DragVector3("Target", state.Target, 0.01f);

        ImGui.Separator();
        if (ImGui.Button("Reset"))
        {
            ImGui.End();
            return OrbitCameraController.CreateDefault();
        }

        ImGui.End();

        return state with
        {
            Distance = distance,
            Yaw = yawDeg * RadPerDeg,
            Pitch = pitchDeg * RadPerDeg,
            Target = target,
        };
    }
}
