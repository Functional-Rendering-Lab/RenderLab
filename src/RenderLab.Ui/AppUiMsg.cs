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
}
