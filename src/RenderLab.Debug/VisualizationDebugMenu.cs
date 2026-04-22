using ImGuiNET;
using RenderLab.Ui;

namespace RenderLab.Debug;

/// <summary>
/// View fragment for the GBuffer visualization selector. Emits
/// <see cref="UiMsg.SetViz"/> on change.
/// </summary>
public static class VisualizationDebugMenu
{
    private static readonly string[] ModeNames =
        Enum.GetNames<VisualizationMode>();

    public static void Draw(VisualizationMode current, Action<UiMsg> dispatch)
    {
        int index = (int)current;
        ImGui.Combo("Buffer", ref index, ModeNames, ModeNames.Length);
        var next = (VisualizationMode)index;
        if (next != current)
            dispatch(new UiMsg.SetViz(next));
    }
}
