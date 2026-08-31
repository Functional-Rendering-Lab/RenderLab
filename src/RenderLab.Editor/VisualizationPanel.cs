using System.Collections.Immutable;
using Ptah.Widgets;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// Which G-buffer channel the pipeline resolves to the screen. One control, because that is the
/// whole panel: the visualization is a single choice, and the panel exists so the choice is
/// somewhere a hand can reach without opening a menu.
/// <para>
/// The list is the pipeline's rather than the enum's. A pipeline with no lighting pass has no
/// Final to resolve, and one that draws a hard-coded triangle has nothing at all; offering a mode
/// that cannot be drawn would be a panel naming one thing while the screen showed another. This
/// is also the panel that used to be a second copy of itself - the G-Buffer pipeline drew its own
/// ImGui combo over the same enum, on its own private field.
/// </para>
/// </summary>
internal static class VisualizationPanel
{
    internal static void Draw(WidgetKit w, WidgetState state, VisualizationMode current,
        ImmutableArray<VisualizationMode> supported, Action<UiMsg> dispatch)
    {
        if (supported.IsEmpty)
        {
            w.DataRow("none", "This pipeline draws straight to the screen.");
            return;
        }

        string[] names = [.. supported.Select(mode => mode.ToString())];

        // An index out of range reads as "nothing chosen", which is what a mode this pipeline
        // cannot draw should look like. The Application moves the model into the supported set
        // when a pipeline is loaded, so it is a state this should not find itself in.
        Edit<int> picked = w.Combo(state.Popups, "Buffer", supported.IndexOf(current), names);

        if (picked.Changed)
            dispatch(new UiMsg.SetViz(supported[picked.Value]));
    }
}
