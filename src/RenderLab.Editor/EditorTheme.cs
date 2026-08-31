using Ptah;

namespace RenderLab.Editor;

/// <summary>
/// RenderLab's palette, written as a Ptah theme. The colours are the ones
/// <c>ImGuiTheme</c> has been applying since the editor got a look of its own - near-black
/// neutral greys with a single ochre accent - so the shell reads as the same tool while it
/// changes hands from one UI framework to the other.
/// <para>
/// The roles are Ptah's, not ImGui's, and the mapping is not one to one: ImGui names a colour
/// per widget part (<c>FrameBg</c>, <c>TitleBg</c>, <c>ScrollbarGrab</c>), Ptah names the job a
/// colour does (<see cref="Theme.Recessed"/> is "content is cut into this", whatever is cut into
/// it). Where the two disagree the role wins, because the role is what keeps a widget nobody has
/// written yet looking like the ones that exist.
/// </para>
/// </summary>
internal static class EditorTheme
{
    /// <summary>
    /// The type size, in logical pixels. Matches the size <c>ImGuiTheme.LoadFont</c> bakes Inter
    /// at, and it is one number rather than three because the atlas, the context, and the theme
    /// all have to agree about it or text is measured at one size and drawn at another.
    /// </summary>
    internal const float FontSize = 16f;

    /// <summary>
    /// The editor's theme. Ptah's own metrics are written for 13-point chrome; these are the
    /// same proportions carried up to 16, which is what keeps a row from reading as a line of
    /// text with no room around it.
    /// </summary>
    internal static readonly Theme Dark = Theme.Warm with
    {
        // Surfaces. Neutral greys: the accent carries the warmth, and a grey that drifts with
        // it is a grey that fights it.
        Recessed = Color.Rgba(28, 28, 28),
        Surface = Color.Rgba(17, 17, 17),
        Chrome = Color.Rgba(10, 10, 10),
        Elevated = Color.Rgba(28, 28, 28),
        RowStripe = Color.Rgba(22, 22, 22),

        // Controls: the face of anything that can be pressed, one clear step above Elevated.
        //
        // This started out as ImGui's button colour - the accent at half alpha over the window,
        // resolved against Surface - and that was reading ImGui's palette rather than Ptah's
        // roles. ImGui names a colour per widget part, so its ochre lands on buttons and on
        // nothing else. Ptah names the job, and Raised is the job every pressable face does: a
        // combo, a scroll grab and a menu row are all raised. Carrying the ochre across meant a
        // drop-down shouted louder than the number field beside it, when the two are the same
        // kind of control holding the same kind of value.
        //
        // So the accent goes back to where the note below already says it belongs - selection,
        // and the marks that have to be found at once - and a control face is a grey.
        Raised = Color.Rgba(44, 44, 44),
        RaisedHot = Color.Rgba(56, 56, 56),
        RaisedDown = Color.Rgba(34, 34, 34),

        // Lines. A seam between two regions is darker than both of them; an outline around a
        // control is lighter than the surface it sits on. ImGui spends one colour on both.
        Line = Color.Rgba(6, 6, 6),
        Edge = Color.Rgba(44, 44, 44),

        // Text.
        Bright = Color.Rgba(240, 240, 240),
        Text = Color.Rgba(214, 214, 214),
        Muted = Color.Rgba(138, 138, 138),

        // Accent. One ochre, spent on selection and on the marks that have to be found at once.
        Selection = Color.Rgba(168, 110, 52),
        Accent = Color.Rgba(212, 144, 74),
        TextSelection = Color.Rgba(201, 123, 48, 110),
        Shadow = Color.Rgba(0, 0, 0, 140),

        // Metrics.
        FontSize = FontSize,
        CornerRadius = 3f,
        RowHeight = 24f,
        BarHeight = 26f,
        Pad = 8f,
        Gap = 5f,
    };
}
