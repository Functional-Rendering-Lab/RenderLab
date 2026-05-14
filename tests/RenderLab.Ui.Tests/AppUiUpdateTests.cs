using RenderLab.Ui;

namespace RenderLab.Ui.Tests;

public class AppUiUpdateTests
{
    private static AppUiModel Fresh() => AppUiModel.Default;

    [Fact]
    public void RequestExit_setsFlag()
    {
        var next = AppUiUpdate.Apply(Fresh(), new AppUiMsg.RequestExit());
        Assert.True(next.RequestedExit);
    }

    [Fact]
    public void TogglePanel_flipsVisibility()
    {
        var start = Fresh();
        var hidden = AppUiUpdate.Apply(start, new AppUiMsg.TogglePanel(PanelId.Lighting));
        Assert.False(hidden.IsPanelVisible(PanelId.Lighting));
        var shown = AppUiUpdate.Apply(hidden, new AppUiMsg.TogglePanel(PanelId.Lighting));
        Assert.True(shown.IsPanelVisible(PanelId.Lighting));
    }

    [Fact]
    public void SetPanelVisible_independentOfOtherPanels()
    {
        var m = AppUiUpdate.Apply(Fresh(), new AppUiMsg.SetPanelVisible(PanelId.Scene, false));
        Assert.False(m.IsPanelVisible(PanelId.Scene));
        Assert.True(m.IsPanelVisible(PanelId.Lighting));
        Assert.True(m.IsPanelVisible(PanelId.Inspector));
    }

    [Fact]
    public void RequestRescanProject_isPassthrough()
    {
        var start = Fresh();
        var next = AppUiUpdate.Apply(start, new AppUiMsg.RequestRescanProject());
        Assert.Equal(start, next);
    }

    [Fact]
    public void RequestRevealInExplorer_isPassthrough()
    {
        var start = Fresh();
        var next = AppUiUpdate.Apply(start, new AppUiMsg.RequestRevealInExplorer("C:/foo/bar.glb"));
        Assert.Equal(start, next);
    }

    [Fact]
    public void ApplyAll_foldsSequence()
    {
        var msgs = new AppUiMsg[]
        {
            new AppUiMsg.TogglePanel(PanelId.Lighting),
            new AppUiMsg.TogglePanel(PanelId.Scene),
            new AppUiMsg.RequestExit(),
        };
        var final = AppUiUpdate.ApplyAll(Fresh(), msgs);
        Assert.False(final.IsPanelVisible(PanelId.Lighting));
        Assert.False(final.IsPanelVisible(PanelId.Scene));
        Assert.True(final.RequestedExit);
    }
}
