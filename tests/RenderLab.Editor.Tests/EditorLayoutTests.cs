using System.Collections.Immutable;
using Ptah.Entities;
using RenderLab.Ui;

namespace RenderLab.Editor.Tests;

/// <summary>
/// The shell layout while the migration is in flight. The tree is derived from the spec every
/// time the set of visible panels changes, and what has to survive that derivation is the two
/// rules nobody can check by looking at the window: a panel Ptah does not draw yet leaves a hole
/// exactly where it will go, and two holes beside each other are one hole - because a boundary
/// between two regions the shell does not own is a splitter that moves nothing and still takes
/// the mouse.
/// </summary>
public class EditorLayoutTests
{
    private static AppUiModel Everything => AppUiModel.Default;

    private static ViewId ViewOf(Panel panel) => panel.Body is PanelBody.Leaf leaf
        ? leaf.View
        : throw new InvalidOperationException($"{panel.Key} is a split, not a leaf");

    [Fact]
    public void APanelThatHasNotMovedYetLeavesAHoleWhereItWillGo()
    {
        PanelTree tree = EditorLayout.Build(Everything);

        // Two columns, not four: the left columns and the viewport hold nothing of Ptah's yet,
        // so they are one hole, and the right column is the only one with a panel in it.
        Assert.Equal(2, tree.Root.Children.Count);
        Assert.Equal(EditorLayout.Hole, ViewOf(tree.Root.Children[0]));

        // Inside that column, Render Graph's place is kept as a hole - its ImGui window is drawn
        // through it - and GPU Timings, which has moved, is a panel under it.
        Panel right = tree.Root.Children[1];
        Assert.Equal(2, right.Children.Count);
        Assert.Equal(EditorLayout.Hole, ViewOf(right.Children[0]));
        Assert.Equal(EditorLayout.ViewOf(PanelId.GpuTimings), ViewOf(right.Children[1]));
    }

    [Fact]
    public void AMergedHoleTakesTheWidthOfEveryColumnItSwallowed()
    {
        PanelTree tree = EditorLayout.Build(Everything);

        // 448 + 506 + 2171 of 3834, which is the dock layout's own arithmetic.
        Assert.Equal(0.815d, tree.Root.Children[0].PercentOfParent, 3);
        Assert.Equal(0.185d, tree.Root.Children[1].PercentOfParent, 3);
    }

    [Fact]
    public void WithNothingOfItsOwnToDrawTheShellIsOneHoleAndNoBoundaries()
    {
        AppUiModel hidden = Everything.WithPanelVisible(PanelId.GpuTimings, false);
        PanelTree tree = EditorLayout.Build(hidden);

        Assert.True(tree.Root.IsLeaf);
        Assert.Equal(EditorLayout.Hole, ViewOf(tree.Root));
    }

    [Fact]
    public void HidingAPanelGivesItsSpaceBackRatherThanLeavingAHoleBehind()
    {
        // The difference between "not ported" and "not visible": the first keeps its place
        // because something else is drawing there, the second gives it up because nothing is.
        AppUiModel noGraph = Everything.WithPanelVisible(PanelId.RenderGraph, false);
        PanelTree tree = EditorLayout.Build(noGraph);

        Panel right = tree.Root.Children[1];
        Assert.True(right.IsLeaf);
        Assert.Equal(EditorLayout.ViewOf(PanelId.GpuTimings), ViewOf(right));
    }

    [Fact]
    public void TheLayoutIsRebuiltWhenAPanelAppearsOrDisappearsAndNotOtherwise()
    {
        // What the view compares each frame to decide whether the tree it is holding still
        // describes the interface that was asked for.
        AppUiModel shown = Everything;
        AppUiModel hidden = shown.WithPanelVisible(PanelId.Scene, false);

        Assert.NotEqual(EditorLayout.LayoutMask(shown), EditorLayout.LayoutMask(hidden));
        Assert.Equal(EditorLayout.LayoutMask(shown), EditorLayout.LayoutMask(shown with
        {
            VisiblePanels = ImmutableHashSet.CreateRange(shown.VisiblePanels),
        }));
    }
}
