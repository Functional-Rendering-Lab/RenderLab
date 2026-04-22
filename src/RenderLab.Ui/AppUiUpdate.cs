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
        AppUiMsg.TogglePanel m       => SetPanel(model, m.Id, !model.IsPanelVisible(m.Id)),
        AppUiMsg.SetPanelVisible m   => SetPanel(model, m.Id, m.Visible),
        _                            => model,
    };

    public static AppUiModel ApplyAll(AppUiModel model, IEnumerable<AppUiMsg> msgs)
    {
        foreach (var msg in msgs) model = Apply(model, msg);
        return model;
    }

    private static AppUiModel SetPanel(AppUiModel m, PanelId id, bool v) => id switch
    {
        PanelId.GpuTimings    => m with { ShowGpuTimings    = v },
        PanelId.Visualization => m with { ShowVisualization = v },
        PanelId.Camera        => m with { ShowCamera        = v },
        PanelId.Lighting      => m with { ShowLighting      = v },
        PanelId.Sphere        => m with { ShowSphere        = v },
        PanelId.RenderGraph   => m with { ShowRenderGraph   = v },
        _                     => m,
    };
}
