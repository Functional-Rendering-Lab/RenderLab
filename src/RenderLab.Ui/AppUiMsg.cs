namespace RenderLab.Ui;

/// <summary>
/// Shell-scope messages emitted by the menu bar: which demo to switch to, which
/// panel to show/hide, and an exit request. Folded into <see cref="AppUiModel"/>
/// by <see cref="AppUiUpdate.Apply"/>.
/// </summary>
public abstract record AppUiMsg
{
    public sealed record RequestSwitchDemo(DemoId Id) : AppUiMsg;
    public sealed record TogglePanel(PanelId Id) : AppUiMsg;
    public sealed record SetPanelVisible(PanelId Id, bool Visible) : AppUiMsg;
    public sealed record RequestExit : AppUiMsg;
    /// <summary>One-shot import trigger (registry side-effect). The reducer
    /// passes through; the shell calls <c>AssetRegistry.ImportGltf</c> and
    /// dispatches <c>UiMsg.AddDrawable</c> for each imported drawable.</summary>
    public sealed record RequestImportGltf(string Path) : AppUiMsg;
}
