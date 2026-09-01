namespace RenderLab.Ui;

/// <summary>
/// Which palette the editor is wearing. The two are Ptah's own stock themes, and this names the
/// choice rather than holding it: a colour belongs to the view layer, and this assembly is the
/// pure model half that must not know a UI framework exists.
/// <para>
/// It is a choice between two named themes rather than a "dark mode" flag because both of these
/// are dark. What they disagree about is temperature - a warm grey with an ochre accent against a
/// true neutral with a steel-blue one - and a boolean would have had to pick one of those as the
/// off position.
/// </para>
/// </summary>
public enum UiTheme
{
    /// <summary>Warm greys, ochre accent, softened corners. Ptah's default.</summary>
    Warm,

    /// <summary>True neutral greys, steel-blue accent, square corners.</summary>
    Neutral,
}
