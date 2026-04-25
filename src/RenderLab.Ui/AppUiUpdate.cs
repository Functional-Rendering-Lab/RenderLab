namespace RenderLab.Ui;

/// <summary>
/// Pure reducer for <see cref="AppUiModel"/>. Matches the shape of
/// <see cref="UiUpdate"/> so tests can fold a message sequence and assert.
/// </summary>
public static class AppUiUpdate
{
    public static AppUiModel Apply(AppUiModel model, AppUiMsg msg) => msg switch
    {
        AppUiMsg.RequestSwitchDemo m => model with { RequestedDemo = m.Id },
        AppUiMsg.RequestExit         => model with { RequestedExit = true },
        AppUiMsg.TogglePanel m       => model.WithPanelVisible(m.Id, !model.IsPanelVisible(m.Id)),
        AppUiMsg.SetPanelVisible m   => model.WithPanelVisible(m.Id, m.Visible),
        _                            => model,
    };

    public static AppUiModel ApplyAll(AppUiModel model, IEnumerable<AppUiMsg> msgs)
    {
        foreach (var msg in msgs) model = Apply(model, msg);
        return model;
    }
}
