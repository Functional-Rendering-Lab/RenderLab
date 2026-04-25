using RenderLab.Ui;

namespace RenderLab.Ui.Tests;

public class AppUiUpdateTests
{
    private static AppUiModel Fresh() => AppUiModel.Default(DemoId.Deferred);

    [Fact]
    public void RequestSwitchDemo_setsRequestedDemo()
    {
        var next = AppUiUpdate.Apply(Fresh(), new AppUiMsg.RequestSwitchDemo(DemoId.Triangle));
        Assert.Equal(DemoId.Triangle, next.RequestedDemo);
        Assert.Equal(DemoId.Deferred, next.CurrentDemo);
    }

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
        var hidden = AppUiUpdate.Apply(start, new AppUiMsg.TogglePanel(PanelId.Camera));
        Assert.False(hidden.IsPanelVisible(PanelId.Camera));
        var shown = AppUiUpdate.Apply(hidden, new AppUiMsg.TogglePanel(PanelId.Camera));
        Assert.True(shown.IsPanelVisible(PanelId.Camera));
    }

    [Fact]
    public void SetPanelVisible_independentOfOtherPanels()
    {
        var m = AppUiUpdate.Apply(Fresh(), new AppUiMsg.SetPanelVisible(PanelId.Sphere, false));
        Assert.False(m.IsPanelVisible(PanelId.Sphere));
        Assert.True(m.IsPanelVisible(PanelId.Camera));
        Assert.True(m.IsPanelVisible(PanelId.Lighting));
    }

    [Fact]
    public void HandOffTo_preservesPanelVisibilityAndClearsRequest()
    {
        var m = Fresh()
            .WithPanelVisible(PanelId.GpuTimings, false)
            with { RequestedDemo = DemoId.GBuffer };
        var next = m.HandOffTo(DemoId.GBuffer);
        Assert.Equal(DemoId.GBuffer, next.CurrentDemo);
        Assert.Null(next.RequestedDemo);
        Assert.False(next.IsPanelVisible(PanelId.GpuTimings));
    }

    [Fact]
    public void ApplyAll_foldsSequence()
    {
        var msgs = new AppUiMsg[]
        {
            new AppUiMsg.TogglePanel(PanelId.Camera),
            new AppUiMsg.TogglePanel(PanelId.Lighting),
            new AppUiMsg.RequestSwitchDemo(DemoId.GBuffer),
        };
        var final = AppUiUpdate.ApplyAll(Fresh(), msgs);
        Assert.False(final.IsPanelVisible(PanelId.Camera));
        Assert.False(final.IsPanelVisible(PanelId.Lighting));
        Assert.Equal(DemoId.GBuffer, final.RequestedDemo);
    }
}
