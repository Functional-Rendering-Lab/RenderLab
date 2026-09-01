using Ptah;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// The editor's palette, which is Ptah's own, at the editor's own density. Not one colour is
/// named here: the shell wears <see cref="Theme.Warm"/> or <see cref="Theme.Neutral"/> with every
/// hex value they ship, and what this class adds is the one axis a palette does not speak to -
/// how big the chrome is.
/// <para>
/// It used to be a palette of its own - the near-black greys and the ochre accent the ImGui
/// editor had been wearing - written as a Ptah theme so the tool would not change appearance
/// while it changed UI framework. That job is over. Carrying a hand-tuned copy past it costs the
/// thing the roles were for: a widget nobody has written yet looks right under a stock theme
/// because the stock theme is what its author had, and every colour overridden here is one that
/// has to be re-tuned by hand when Ptah tunes its own.
/// </para>
/// <para>
/// Density is a different axis, and it belongs to the display rather than to the palette. Ptah's
/// stock metrics are written around 13-point chrome, which is right for a tool on a 1080p panel
/// and too small to read on the 4K workstation this editor is used on. So the type size is the
/// editor's to choose, and everything measured in multiples of it follows in the proportions the
/// stock theme set - which is what keeps a row from reading as a line of text with no room around
/// it. Corner radius, hairlines and shadows are left alone: those are the theme's identity or its
/// physics, not its scale.
/// </para>
/// </summary>
internal static class EditorTheme
{
    /// <summary>
    /// The type size the shell is measured, baked and drawn at, in logical pixels. One number
    /// rather than three because the atlas, the context and the theme all have to agree about it
    /// or text is measured at one size and drawn at another.
    /// <para>
    /// It is also why a theme the shell can switch to has to agree with the one it started on
    /// about <see cref="Theme.FontSize"/>: the atlas is baked once, at startup, and a palette swap
    /// does not get to rebake it. Both stock themes are brought to this one size below, so the
    /// switch stays free.
    /// </para>
    /// </summary>
    internal const float FontSize = 16f;

    /// <summary>How much larger than stock the editor's chrome is. See the class remarks.</summary>
    private static readonly float Density = FontSize / Theme.Default.FontSize;

    /// <summary>The theme a <see cref="UiTheme"/> stands for, at the editor's density.</summary>
    internal static Theme Of(UiTheme choice) => AtEditorDensity(choice switch
    {
        UiTheme.Neutral => Theme.Neutral,
        _ => Theme.Warm,
    });

    private static Theme AtEditorDensity(Theme theme) => theme with
    {
        FontSize = FontSize,
        RowHeight = Step(theme.RowHeight),
        BarHeight = Step(theme.BarHeight),
        Pad = Step(theme.Pad),
        Gap = Step(theme.Gap),
        Indent = Step(theme.Indent),
        SplitterThickness = Step(theme.SplitterThickness),
        GripSize = Step(theme.GripSize),
        ScrollBarWidth = Step(theme.ScrollBarWidth),
    };

    /// <summary>
    /// One stock metric carried up to the editor's type size. Rounded to whole pixels, because a
    /// row height landing on a half pixel is a row of text landing on one.
    /// </summary>
    private static float Step(float metric) => MathF.Round(metric * Density);
}
