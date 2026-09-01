namespace RenderLab.Ui;

/// <summary>
/// Pure reducer for <see cref="AppUiModel"/>. Matches the shape of
/// <see cref="UiUpdate"/> so tests can fold a message sequence and assert.
/// All project / scene / asset side-effecting messages pass through — the
/// shell intercepts them before <see cref="ApplyAll"/> runs.
/// </summary>
public static class AppUiUpdate
{
    public static AppUiModel Apply(AppUiModel model, AppUiMsg msg) => msg switch
    {
        AppUiMsg.RequestExit             => model with { RequestedExit = true },
        AppUiMsg.TogglePanel m           => model.WithPanelVisible(m.Id, !model.IsPanelVisible(m.Id)),
        AppUiMsg.SetPanelVisible m       => model.WithPanelVisible(m.Id, m.Visible),
        AppUiMsg.ToggleTheme             => model.WithThemeToggled(),
        AppUiMsg.RequestImportGltf       => model,
        AppUiMsg.RequestImportGltfDialog => model,
        AppUiMsg.RequestRemoveMesh       => model,
        AppUiMsg.RequestRemoveTexture    => model,
        AppUiMsg.RequestRemoveMaterial   => model,
        AppUiMsg.RequestSaveScene        => model,
        AppUiMsg.RequestSaveSceneAs      => model,
        AppUiMsg.RequestOpenProjectDialog => model,
        AppUiMsg.RequestOpenProject      => model,
        AppUiMsg.RequestOpenScene        => model,
        AppUiMsg.RequestReloadScene      => model,
        AppUiMsg.RequestNewProjectDialog => model,
        AppUiMsg.RequestRescanProject    => model,
        AppUiMsg.RequestRevealInExplorer => model,
        _                                => model,
    };

    public static AppUiModel ApplyAll(AppUiModel model, IEnumerable<AppUiMsg> msgs)
    {
        foreach (var msg in msgs) model = Apply(model, msg);
        return model;
    }
}
