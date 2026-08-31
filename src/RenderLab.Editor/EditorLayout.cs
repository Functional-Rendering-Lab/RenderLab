using System.Collections.Immutable;
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
/// showing through the middle; Render Graph and GPU Timings on the right - with the docking
/// machinery that produced it left behind. Nothing here is tabbed and nothing here is
/// rearrangeable, because nothing in the layout the tool actually settled on was either. What a
/// boundary between two panels still does is move, and that costs no code: a panel's size is a
/// share of its parent, so a window resize is arithmetic and a drag is a new share.
/// </para>
/// <para>
/// The spec names every panel in its final place from the first day of the migration, and
/// <see cref="Ported"/> says which of them Ptah draws today. A panel that has not moved over yet
/// leaves a hole exactly where it will go, and Dear ImGui keeps drawing it there. Porting one is
/// one entry in that set.
/// </para>
/// </summary>
public static class EditorLayout
{
    /// <summary>
    /// Every hole is the same view, because a hole is not an interface - it is the part of the
    /// window Ptah does not draw. Two of them side by side are one hole for the same reason: a
    /// boundary between two regions the shell does not own is a splitter that would move nothing.
    /// </summary>
    public static readonly ViewId Hole = new("hole");

    /// <summary>
    /// The panels the Ptah shell draws. Everything else in <see cref="Columns"/> is still a Dear
    /// ImGui window, drawn over the hole its place in the layout leaves.
    /// </summary>
    public static readonly ImmutableHashSet<PanelId> Ported = ImmutableHashSet.Create(
        PanelId.GpuTimings,
        PanelId.Visualization,
        PanelId.Lighting,
        PanelId.Inspector,
        PanelId.Scene,
        PanelId.AssetBrowser,
        PanelId.Project);

    /// <summary>A panel's share of the column it is in. Shares are relative; only their ratio matters.</summary>
    private readonly record struct PanelSpec(PanelId Panel, float Share);

    /// <summary>A column of panels, and its share of the window's width. An empty column is the viewport.</summary>
    private readonly record struct ColumnSpec(float Width, PanelSpec[] Panels);

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

        // The viewport: no panels, because the scene is not one. It is the hole the layout
        // leaves, and the application has already drawn into it by the time the shell records.
        new(2171f, []),

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

    /// <summary>The panel a view belongs to, or None for <see cref="Hole"/>.</summary>
    public static Optional<PanelId> PanelOf(ViewId view)
    {
        int index = Array.IndexOf(Views, view);
        return index < 0 ? Optional<PanelId>.None : Optional.Some((PanelId)index);
    }

    /// <summary>Which views are holes rather than panels. Handed to <c>WidgetKit.PanelArea</c>.</summary>
    public static bool IsHole(ViewId view) => view == Hole;

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

    /// <summary>One leaf of the tree as it will be built: a panel's view, or a hole.</summary>
    private readonly record struct LeafSpec(ViewId View, float Share);

    /// <summary>A column as it will be built. No leaves at all means the whole column is a hole.</summary>
    private readonly record struct Slot(float Width, LeafSpec[] Leaves);

    private static List<Slot> Slots(AppUiModel app)
    {
        var slots = new List<Slot>();

        foreach (ColumnSpec column in Columns)
        {
            LeafSpec[] leaves = Leaves(app, column);

            if (leaves.Length > 0)
            {
                slots.Add(new Slot(column.Width, leaves));
                continue;
            }

            // A column with nothing of Ptah's in it is a hole, and a hole beside a hole is one
            // hole: the viewport and the columns that are still ImGui's are one region as far as
            // this layout is concerned, and a boundary drawn inside it would move nothing.
            if (slots.Count > 0 && slots[^1].Leaves.Length == 0)
                slots[^1] = slots[^1] with { Width = slots[^1].Width + column.Width };
            else
                slots.Add(new Slot(column.Width, []));
        }

        return slots;
    }

    /// <summary>
    /// One column's leaves: a panel for each ported entry, and one hole for each run of entries
    /// that are still ImGui's - so a panel that has not moved yet keeps its place in the layout
    /// and its window shows through the gap left for it. A hidden panel is in neither, because
    /// hiding one is meant to give its space back.
    /// </summary>
    private static LeafSpec[] Leaves(AppUiModel app, ColumnSpec column)
    {
        var leaves = new List<LeafSpec>();
        bool ours = false;

        foreach (PanelSpec spec in column.Panels)
        {
            if (!app.IsPanelVisible(spec.Panel))
                continue;

            if (Ported.Contains(spec.Panel))
            {
                leaves.Add(new LeafSpec(ViewOf(spec.Panel), spec.Share));
                ours = true;
                continue;
            }

            if (leaves.Count > 0 && IsHole(leaves[^1].View))
                leaves[^1] = leaves[^1] with { Share = leaves[^1].Share + spec.Share };
            else
                leaves.Add(new LeafSpec(Hole, spec.Share));
        }

        return ours ? [.. leaves] : [];
    }

    private static ViewId FirstView(Slot slot) =>
        slot.Leaves.Length == 0 ? Hole : slot.Leaves[0].View;
}
