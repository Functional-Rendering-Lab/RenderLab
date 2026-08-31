using System.Collections.Immutable;
using Ptah.Entities;
using RenderLab.Ui;

namespace RenderLab.Editor.Tests;

/// <summary>
/// The shell layout. The tree is derived from the spec every time the set of visible panels
/// changes, and what has to survive that derivation is the rules nobody can check by looking at
/// the window: a hidden panel gives its space back rather than keeping a place nothing draws in,
/// a column left with nothing showing becomes a hole, and holes beside each other are one hole -
/// because a boundary between two regions the shell does not own is a splitter that moves nothing
/// and still takes the mouse.
/// <para>
/// These used to be about the migration - a panel Dear ImGui still drew left a hole where it
/// would go, and the shapes here moved as panels ported. Every panel has moved, so what is left
/// is the part that was never temporary: the viewport is a hole, and so is a column somebody has
/// emptied.
/// </para>
/// </summary>
public class EditorLayoutTests
{
    private static AppUiModel Everything => AppUiModel.Default;

    private static ViewId ViewOf(Panel panel) => panel.Body is PanelBody.Leaf leaf
        ? leaf.View
        : throw new InvalidOperationException($"{panel.Key} is a split, not a leaf");

    [Fact]
    public void EveryColumnHoldingAPanelIsAColumnOfTheTree()
    {
        PanelTree tree = EditorLayout.Build(Everything);

        // Four columns: the two on the left and the one on the right each hold something of
        // Ptah's now, and the viewport between them holds nothing by design.
        Assert.Equal(4, tree.Root.Children.Count);

        // 448 / 506 / 2171 / 709 of 3834, which is the dock layout's own arithmetic.
        Assert.Equal(0.117d, tree.Root.Children[0].PercentOfParent, 3);
        Assert.Equal(0.132d, tree.Root.Children[1].PercentOfParent, 3);
        Assert.Equal(0.566d, tree.Root.Children[2].PercentOfParent, 3);
        Assert.Equal(0.185d, tree.Root.Children[3].PercentOfParent, 3);
    }

    [Fact]
    public void TheRightColumnIsRenderGraphOverGpuTimings()
    {
        PanelTree tree = EditorLayout.Build(Everything);

        // The last two panels to move across, and the last hole in the layout that was not the
        // viewport: Render Graph was drawn through one of these until Phase 4.
        Panel right = tree.Root.Children[3];
        Assert.Equal(2, right.Children.Count);
        Assert.Equal(EditorLayout.ViewOf(PanelId.RenderGraph), ViewOf(right.Children[0]));
        Assert.Equal(EditorLayout.ViewOf(PanelId.GpuTimings), ViewOf(right.Children[1]));
    }

    [Fact]
    public void TheColumnsThatArePtahsHoldNoHolesAtAll()
    {
        PanelTree tree = EditorLayout.Build(Everything);

        // The left column, whole: the two forms at the top and the two outliners under them, in
        // the order the spec names, and nothing of anybody else's between them.
        Panel left = tree.Root.Children[0];
        Assert.Equal(4, left.Children.Count);
        Assert.Equal(EditorLayout.ViewOf(PanelId.Visualization), ViewOf(left.Children[0]));
        Assert.Equal(EditorLayout.ViewOf(PanelId.Lighting), ViewOf(left.Children[1]));
        Assert.Equal(EditorLayout.ViewOf(PanelId.Scene), ViewOf(left.Children[2]));
        Assert.Equal(EditorLayout.ViewOf(PanelId.Project), ViewOf(left.Children[3]));

        // 0.7 / 1.0 / 4.6 / 3.7 of 10: a form gets its rows and a margin, and the lists get the
        // rest, which is what the shares were retuned to say.
        Assert.Equal(0.07d, left.Children[0].PercentOfParent, 3);
        Assert.Equal(0.10d, left.Children[1].PercentOfParent, 3);
        Assert.Equal(0.46d, left.Children[2].PercentOfParent, 3);
        Assert.Equal(0.37d, left.Children[3].PercentOfParent, 3);

        Panel middle = tree.Root.Children[1];
        Assert.Equal(2, middle.Children.Count);
        Assert.Equal(EditorLayout.ViewOf(PanelId.Inspector), ViewOf(middle.Children[0]));
        Assert.Equal(EditorLayout.ViewOf(PanelId.AssetBrowser), ViewOf(middle.Children[1]));
        Assert.Equal(0.55d, middle.Children[0].PercentOfParent, 3);
        Assert.Equal(0.45d, middle.Children[1].PercentOfParent, 3);
    }

    [Fact]
    public void AColumnEmptiedByHidingItsPanelsJoinsTheHoleBesideIt()
    {
        // Untick both of the right column's panels and the column has nothing to draw, so it is
        // a hole - and a hole beside the viewport is the viewport, because a boundary drawn
        // inside it would move nothing.
        AppUiModel emptied = Everything
            .WithPanelVisible(PanelId.GpuTimings, false)
            .WithPanelVisible(PanelId.RenderGraph, false);

        PanelTree tree = EditorLayout.Build(emptied);

        Assert.Equal(3, tree.Root.Children.Count);
        Assert.Equal(EditorLayout.Hole, ViewOf(tree.Root.Children[2]));

        // 2171 + 709 of 3834: a merged hole takes the width of every column it swallowed.
        Assert.Equal(0.751d, tree.Root.Children[2].PercentOfParent, 3);
    }

    [Fact]
    public void WithNothingOfItsOwnToDrawTheShellIsOneHoleAndNoBoundaries()
    {
        AppUiModel hidden = Everything;
        foreach (PanelId id in Enum.GetValues<PanelId>())
            hidden = hidden.WithPanelVisible(id, false);

        PanelTree tree = EditorLayout.Build(hidden);

        Assert.True(tree.Root.IsLeaf);
        Assert.Equal(EditorLayout.Hole, ViewOf(tree.Root));
    }

    [Fact]
    public void HidingAPanelGivesItsSpaceBackRatherThanLeavingAHoleBehind()
    {
        AppUiModel noGraph = Everything.WithPanelVisible(PanelId.RenderGraph, false);
        PanelTree tree = EditorLayout.Build(noGraph);

        Panel right = tree.Root.Children[^1];
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
