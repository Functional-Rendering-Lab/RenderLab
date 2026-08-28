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

        // Controls. ImGui draws a button as the accent at half alpha over the window, which
        // reads as a tinted face rather than a coloured one; these are that blend resolved
        // against Surface, so a button keeps its look without depending on what is behind it.
        Raised = Color.Rgba(114, 73, 33),
        RaisedHot = Color.Rgba(152, 95, 40),
        RaisedDown = Color.Rgba(88, 57, 26),

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
