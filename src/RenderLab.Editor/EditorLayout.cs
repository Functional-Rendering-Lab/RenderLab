using Ptah;
using Ptah.Entities;
using Ptah.Functional;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// The editor's shell layout: four columns, written once, in code.
/// <para>
/// This is the same three-column arrangement the dock ini has been saving - Visualization,
/// Lighting, Scene and Project on the left; Inspector and Asset Browser beside them; the scene
/// in the middle; Render Graph and GPU Timings on the right - with the docking
/// machinery that produced it left behind. Nothing here is tabbed and nothing here is
/// rearrangeable, because nothing in the layout the tool actually settled on was either. What a
/// boundary between two panels still does is move, and that costs no code: a panel's size is a
/// share of its parent, so a window resize is arithmetic and a drag is a new share.
/// </para>
/// <para>
/// Two of the leaves are not panels. The viewport is one: the scene arrives there as a picture,
/// so it is a leaf with no header and an image filling it. An emptied column is the other -
/// hiding every panel in one leaves a region with nothing to show, and it is drawn as the shell's
/// own ground rather than as a panel with nothing in it.
/// </para>
/// </summary>
public static class EditorLayout
{
    /// <summary>
    /// Where the scene is shown. It used to be a hole - a rectangle the shell left unpainted so
    /// that the frame the renderer had already drawn across the whole window showed through it -
    /// and it is a picture now: the renderer draws into an image the size of this leaf, and this
    /// leaf draws that image. What that buys is a scene the shape of the panel it is looked at
    /// through, and a shell that is free to put something in front of it.
    /// </summary>
    public static readonly ViewId Viewport = new("viewport");

    /// <summary>
    /// A column with nothing left in it. Hiding every panel in one has to leave something, and
    /// what it leaves is the shell's ground: a header over an empty body would offer to close a
    /// panel that is not there, and the alternative - collapsing the column away - is a layout
    /// that reflows when a panel is hidden and cannot put it back where it was.
    /// <para>
    /// Two of them side by side are one region, and one beside the viewport joins it: a boundary
    /// between two regions with nothing in them is a splitter that would move nothing and still
    /// take the mouse.
    /// </para>
    /// </summary>
    public static readonly ViewId Empty = new("empty");

    /// <summary>A panel's share of the column it is in. Shares are relative; only their ratio matters.</summary>
    private readonly record struct PanelSpec(PanelId Panel, float Share);

    /// <summary>
    /// A column of panels, and its share of the window's width. <c>Viewport</c> names the one
    /// column that holds the scene rather than panels, which the spec has to say: a column with
    /// no panels in it is otherwise indistinguishable from one whose panels are all hidden, and
    /// those two are not the same thing to draw.
    /// </summary>
    private readonly record struct ColumnSpec(float Width, PanelSpec[] Panels, bool Viewport = false);

    /// <summary>
    /// The layout. The widths are the pixel sizes the dock layout settled on, kept as weights
    /// because a share is what survives a change of monitor. The heights are not: transcribing
    /// them from a 4K session gives a panel three percent of the window, which is a title bar
    /// and nothing under it on a smaller screen, so the vertical shares say what each panel
    /// needs rather than what it happened to have.
    /// <para>
    /// Which is a claim that could not be checked until the panels drew something. Visualization
    /// is one row and Lighting is two, and their first shares gave the pair a third of the
    /// column - nearly all of it empty, and taken from the two outliners beside them, which are
    /// lists and can always use the space. A form needs its rows and a margin; a list needs
    /// whatever is left.
    /// </para>
    /// </summary>
    private static readonly ColumnSpec[] Columns =
    [
        new(448f,
        [
            new(PanelId.Visualization, 0.7f),
            new(PanelId.Lighting, 1.0f),
            new(PanelId.Scene, 4.6f),
            new(PanelId.Project, 3.7f),
        ]),
        new(506f,
        [
            new(PanelId.Inspector, 5.5f),
            new(PanelId.AssetBrowser, 4.5f),
        ]),

        // The viewport: no panels, because the scene is not one. What goes here is the image the
        // renderer drew, at exactly the size this column came out.
        new(2171f, [], Viewport: true),

        new(709f,
        [
            new(PanelId.RenderGraph, 6f),
            new(PanelId.GpuTimings, 4f),
        ]),
    ];

    private static readonly ViewId[] Views =
        [.. Enum.GetValues<PanelId>().Select(id => new ViewId(id.ToString()))];

    /// <summary>The view a panel instantiates. One per <see cref="PanelId"/>, and stable.</summary>
    public static ViewId ViewOf(PanelId id) => Views[(int)id];

    /// <summary>The panel a view belongs to, or None for a view that is not one.</summary>
    public static Optional<PanelId> PanelOf(ViewId view)
    {
        int index = Array.IndexOf(Views, view);
        return index < 0 ? Optional<PanelId>.None : Optional.Some((PanelId)index);
    }

    /// <summary>
    /// Which views get no panel chrome. Handed to <c>WidgetKit.PanelArea</c> as its passthrough
    /// predicate: neither the viewport nor an emptied column has a title to put in a header or a
    /// panel to offer to close.
    /// </summary>
    public static bool IsChromeless(ViewId view) => view == Viewport || view == Empty;

    /// <summary>What a panel is called in its header. The names the tool has always used.</summary>
    public static string TitleOf(ViewId view) => PanelOf(view).Match(
        some: id => id switch
        {
            PanelId.GpuTimings => "GPU Timings",
            PanelId.Visualization => "Visualization",
            PanelId.Lighting => "Lighting",
            PanelId.RenderGraph => "Render Graph",
            PanelId.Scene => "Scene",
            PanelId.AssetBrowser => "Asset Browser",
            PanelId.Project => "Project",
            PanelId.Inspector => "Inspector",
            _ => id.ToString(),
        },
        none: () => string.Empty);

    /// <summary>
    /// Which panels are in the layout at all - the visible ones, whether they are drawn by Ptah
    /// or still by ImGui, since either way they take up a place in it. A bit per
    /// <see cref="PanelId"/>, so a frame can tell in one comparison whether the tree it is
    /// holding still describes the interface that was asked for.
    /// </summary>
    public static int LayoutMask(AppUiModel app)
    {
        int mask = 0;
        foreach (PanelId id in Enum.GetValues<PanelId>())
            if (app.IsPanelVisible(id))
                mask |= 1 << (int)id;

        return mask;
    }

    /// <summary>
    /// Builds the tree the shell walks this frame. The spec is the source of truth, so this is
    /// always available as the simple answer to a layout change: a panel that appears or
    /// disappears rebuilds the tree rather than patching it. What that costs is the boundary
    /// drags, which go back to the authored shares - and the shares are authored, so there is
    /// nothing else to lose.
    /// </summary>
    public static PanelTree Build(AppUiModel app)
    {
        List<Slot> slots = Slots(app);

        var tree = new PanelTree(FirstView(slots[0]));

        for (int i = 1; i < slots.Count; i++)
        {
            // Applied one at a time so that each split targets the column the last one made,
            // which is what makes them siblings: a split whose parent already divides along the
            // same axis joins it rather than nesting inside it.
            tree.RequestSplit(LastChild(tree.Root), Axis2.X, FirstView(slots[i]));
            tree.ApplyCommands();
        }

        float[] widths = Shares([.. slots.Select(slot => slot.Width)]);

        for (int i = 0; i < slots.Count; i++)
        {
            Panel column = slots.Count == 1 ? tree.Root : tree.Root.Children[i];
            column.SetPercent(widths[i]);
            Stack(tree, column, slots[i].Leaves);
        }

        return tree;
    }

    /// <summary>Divides one column between its leaves, top to bottom.</summary>
    private static void Stack(PanelTree tree, Panel column, LeafSpec[] leaves)
    {
        for (int i = 1; i < leaves.Length; i++)
        {
            tree.RequestSplit(LastChild(column), Axis2.Y, leaves[i].View);
            tree.ApplyCommands();
        }

        if (leaves.Length < 2)
            return;

        float[] heights = Shares([.. leaves.Select(leaf => leaf.Share)]);
        for (int i = 0; i < leaves.Length; i++)
            column.Children[i].SetPercent(heights[i]);
    }

    private static Panel LastChild(Panel panel) => panel.IsLeaf ? panel : panel.Children[^1];

    private static float[] Shares(float[] weights)
    {
        float total = 0f;
        foreach (float weight in weights)
            total += weight;

        float[] shares = new float[weights.Length];
        for (int i = 0; i < weights.Length; i++)
            shares[i] = total > 0f ? weights[i] / total : 1f / weights.Length;

        return shares;
    }

    /// <summary>One leaf of the tree as it will be built: a panel's view, or a chromeless one.</summary>
    private readonly record struct LeafSpec(ViewId View, float Share);

    /// <summary>
    /// A column as it will be built. No leaves at all means the column shows no panels, and
    /// <c>Viewport</c> says whether the scene goes there or nothing does.
    /// </summary>
    private readonly record struct Slot(float Width, LeafSpec[] Leaves, bool Viewport);

    private static List<Slot> Slots(AppUiModel app)
    {
        var slots = new List<Slot>();

        foreach (ColumnSpec column in Columns)
        {
            LeafSpec[] leaves = Leaves(app, column);

            if (leaves.Length > 0)
            {
                slots.Add(new Slot(column.Width, leaves, column.Viewport));
                continue;
            }

            // A column with nothing showing in it merges into the one beside it if that one has
            // nothing showing either: a boundary between two regions with no panels in them would
            // move nothing and still take the mouse. Merging with the viewport hands the viewport
            // the space, which is what emptying the column next to it ought to do.
            if (slots.Count > 0 && slots[^1].Leaves.Length == 0)
                slots[^1] = slots[^1] with
                {
                    Width = slots[^1].Width + column.Width,
                    Viewport = slots[^1].Viewport || column.Viewport,
                };
            else
                slots.Add(new Slot(column.Width, [], column.Viewport));
        }

        return slots;
    }

    /// <summary>
    /// One column's leaves: the panels in it that are showing, in the order the spec names them.
    /// A hidden panel is not a leaf at all, because hiding one is meant to give its space back to
    /// the panels around it rather than to leave a gap nothing draws in.
    /// </summary>
    private static LeafSpec[] Leaves(AppUiModel app, ColumnSpec column) =>
    [
        .. column.Panels
            .Where(spec => app.IsPanelVisible(spec.Panel))
            .Select(spec => new LeafSpec(ViewOf(spec.Panel), spec.Share)),
    ];

    private static ViewId FirstView(Slot slot) =>
        slot.Leaves.Length > 0 ? slot.Leaves[0].View
        : slot.Viewport ? Viewport
        : Empty;
}
