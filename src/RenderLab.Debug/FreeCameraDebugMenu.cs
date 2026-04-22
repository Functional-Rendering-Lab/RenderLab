using System.Numerics;
using ImGuiNET;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Debug;

/// <summary>
/// View fragment for the camera panel. Reads <see cref="FreeCameraState"/>, renders
/// ImGui widgets, and dispatches an <see cref="UiMsg.UpdateCamera"/> message when
/// the user edits a field. Pure value-in, message-out — no state of its own.
/// </summary>
public static class FreeCameraDebugMenu
{
    private const float DegPerRad = 180f / MathF.PI;
    private const float RadPerDeg = MathF.PI / 180f;

    public static void Draw(FreeCameraState state, Action<UiMsg> dispatch)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 160), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 200), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Camera"))
        {
            ImGui.End();
            return;
        }

        var position = DebugFields.DragVector3("Position", state.Position, 0.05f);

        float yawDeg = state.Yaw * DegPerRad;
        float pitchDeg = state.Pitch * DegPerRad;
        yawDeg = DebugFields.DragFloat("Yaw", yawDeg, 0.5f, format: "%.1f deg");
        pitchDeg = DebugFields.DragFloat("Pitch", pitchDeg, 0.5f, -89.9f, 89.9f, "%.1f deg");

        ImGui.Separator();
        bool reset = ImGui.Button("Reset");

        ImGui.End();

        if (reset)
        {
            dispatch(new UiMsg.UpdateCamera(FreeCameraController.CreateDefault()));
            return;
        }

        var next = state with
        {
            Position = position,
            Yaw = yawDeg * RadPerDeg,
            Pitch = pitchDeg * RadPerDeg,
        };

        if (!next.Equals(state))
            dispatch(new UiMsg.UpdateCamera(next));
    }
}
