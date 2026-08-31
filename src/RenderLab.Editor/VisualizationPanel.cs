using Ptah.Widgets;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// Which G-buffer channel the deferred pass resolves to the screen. One control, because that is
/// the whole panel: the visualization is a single choice, and the panel exists so the choice is
/// somewhere a hand can reach without opening a menu.
/// </summary>
internal static class VisualizationPanel
{
    private static readonly string[] Modes = Enum.GetNames<VisualizationMode>();

    internal static void Draw(WidgetKit w, WidgetState state, VisualizationMode current,
        Action<UiMsg> dispatch)
    {
        Edit<int> mode = w.Combo(state.Popups, "Buffer", (int)current, Modes);
        if (mode.Changed)
            dispatch(new UiMsg.SetViz((VisualizationMode)mode.Value));
    }
}
