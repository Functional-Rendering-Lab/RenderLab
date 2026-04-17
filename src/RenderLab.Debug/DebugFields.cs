using System.Numerics;
using ImGuiNET;

namespace RenderLab.Debug;

/// <summary>
/// Functional wrappers over ImGui's ref-based widget API.
/// Each method takes an immutable value, renders a widget, and returns the (potentially modified) value.
/// Compose these in debug menu Draw methods to build two-way bound panels.
/// </summary>
public static class DebugFields
{
    public static float DragFloat(string label, float value, float speed = 0.01f,
        float min = float.MinValue, float max = float.MaxValue, string format = "%.3f")
    {
        ImGui.DragFloat(label, ref value, speed, min, max, format);
        return value;
    }

    public static float SliderFloat(string label, float value, float min, float max,
        string format = "%.3f", ImGuiSliderFlags flags = ImGuiSliderFlags.None)
    {
        ImGui.SliderFloat(label, ref value, min, max, format, flags);
        return value;
    }

    public static float InputFloat(string label, float value, float step = 0f,
        float stepFast = 0f, string format = "%.3f")
    {
        ImGui.InputFloat(label, ref value, step, stepFast, format);
        return value;
    }

    public static int DragInt(string label, int value, float speed = 1f,
        int min = int.MinValue, int max = int.MaxValue)
    {
        ImGui.DragInt(label, ref value, speed, min, max);
        return value;
    }

    public static int SliderInt(string label, int value, int min, int max)
    {
        ImGui.SliderInt(label, ref value, min, max);
        return value;
    }

    public static bool Checkbox(string label, bool value)
    {
        ImGui.Checkbox(label, ref value);
        return value;
    }

    public static Vector3 DragVector3(string label, Vector3 value, float speed = 0.01f,
        float min = float.MinValue, float max = float.MaxValue, string format = "%.3f")
    {
        ImGui.DragFloat3(label, ref value, speed, min, max, format);
        return value;
    }

    public static Vector3 InputVector3(string label, Vector3 value,
        string format = "%.3f")
    {
        ImGui.InputFloat3(label, ref value, format);
        return value;
    }

    public static Vector4 ColorEdit(string label, Vector4 value)
    {
        ImGui.ColorEdit4(label, ref value);
        return value;
    }

    public static Vector3 ColorEdit(string label, Vector3 value)
    {
        ImGui.ColorEdit3(label, ref value);
        return value;
    }
}
