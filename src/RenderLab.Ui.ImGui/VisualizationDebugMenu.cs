using RenderLab.Ui;

namespace RenderLab.Ui.ImGui;

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
        var next = (VisualizationMode)DebugFields.ComboEdit("Buffer", (int)current, ModeNames);
        if (next != current)
            dispatch(new UiMsg.SetViz(next));
    }
}
