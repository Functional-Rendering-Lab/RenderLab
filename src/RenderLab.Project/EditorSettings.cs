namespace RenderLab.Project;

/// <summary>
/// Per-user editor preferences persisted to
/// <c>%LOCALAPPDATA%/RenderLab/editor.json</c> (or the platform equivalent
/// returned by <see cref="System.Environment.SpecialFolder.LocalApplicationData"/>).
/// Restored on launch when no project path argv is supplied so the editor
/// resumes where the user left off; persisted on project / scene change and
/// on shutdown.
/// </summary>
/// <param name="Version">Schema version. Reserved for forward-compatibility — only <c>1</c> is recognised today.</param>
/// <param name="LastProjectPath">Absolute path of the most recently opened project. Empty on a clean install.</param>
/// <param name="LastScenePath">Project-relative scene path active when the editor closed. Empty for non-scene pipelines.</param>
/// <param name="HiddenPanels">Names of <c>PanelId</c> values the user toggled off — every other panel defaults to visible.</param>
/// <param name="Theme">
/// Name of the <c>UiTheme</c> the shell was wearing. A name rather than the value for the reason
/// <paramref name="HiddenPanels"/> is: this assembly holds the file format and not the editor's
/// vocabulary. Empty - which is what a file written before the theme could be switched says -
/// means whichever theme the model defaults to.
/// </param>
public sealed record EditorSettings(
    int Version,
    string LastProjectPath,
    string LastScenePath,
    string[] HiddenPanels,
    string Theme)
{
    public static EditorSettings Default { get; } = new(
        Version: 1,
        LastProjectPath: "",
        LastScenePath: "",
        HiddenPanels: [],
        Theme: "");
}
