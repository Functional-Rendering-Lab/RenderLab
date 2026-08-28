using System.Collections.Immutable;
using Ptah;
using Ptah.Entities;
using Ptah.Functional;
using Ptah.Widgets;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// The Ptah half of the editor's view layer: the shell layout, the panels that have moved over
/// to it, and the messages they emit. The counterpart of <c>RenderLab.Ui.ImGui.UiView</c>, and
/// deliberately the same shape - it takes the model and hands back a <see cref="UiViewResult"/>,
/// so the shell folds one kind of result whichever framework produced it, and the day the last
/// panel moves the other one is deleted rather than untangled.
/// <para>
/// It holds the panel tree, because a tree is where a boundary drag lives and a drag has to
/// survive the frame that noticed it. Everything else about a frame is built from the model and
/// thrown away.
/// </para>
/// </summary>
public sealed class EditorView
{
    private readonly Theme _theme = EditorTheme.Dark;

    /// <summary>
    /// The boxes the layout left holes in, this frame. What the pointer is over is the whole of
    /// <see cref="UiIntent.WantCaptureMouse"/>, and a hole is the one region of the window the
    /// shell does not want it: the scene is under one, and the panels that have not been ported
    /// yet are drawn over the others.
    /// </summary>
    private readonly List<UIBox> _holes = [];

    private PanelTree _tree;
    private int _layout;

    public EditorView()
    {
        // Starts holding a tree that is all hole, so the first frame's live set is a change like
        // any other and there is one codepath that ever builds a tree.
        var hidden = AppUiModel.Default with { VisiblePanels = ImmutableHashSet<PanelId>.Empty };
        _layout = EditorLayout.LayoutMask(hidden);
        _tree = EditorLayout.Build(hidden);
    }

    public UiViewResult Draw(UIContext ui, AppUiModel app, FrameStats stats, UiCost cost)
    {
        int layout = EditorLayout.LayoutMask(app);
        if (layout != _layout)
        {
            _layout = layout;
            _tree = EditorLayout.Build(app);
        }

        var appMessages = new List<AppUiMsg>();
        var messages = new List<UiMsg>();
        _holes.Clear();

        var w = new WidgetKit(ui, _theme);

        // Transparent chrome, because this is composited over a frame the renderer has already
        // drawn: a hole in a leaf shows nothing through if the split above it is painting over
        // the same rectangle.
        // Close and nothing else on a header: this layout is authored in code, and a split made
        // with the mouse would have nowhere to be authored back to.
        w.PanelArea(_tree, EditorLayout.TitleOf, BuildLeaf,
                Optional.Some<Func<ViewId, bool>>(EditorLayout.IsHole),
                Optional.Some(Color.Transparent),
                Optional.Some(PanelHeaderButtons.Close))
            .IfSome(Requested);

        // The frame's mutation phase, after the build, when nothing is walking the tree: a
        // boundary noticed during the build moves the next one.
        _tree.Commit();
        _tree.ApplyCommands();

        return new UiViewResult(appMessages, messages,
            new UiIntent(WantCaptureMouse: !OverHole(ui), WantCaptureKeyboard: ui.TextInputFocused));

        void BuildLeaf(Panel panel, ViewId view)
        {
            if (EditorLayout.IsHole(view))
            {
                _holes.Add(ui.TopParent);
                return;
            }

            EditorLayout.PanelOf(view).IfSome(Body);
        }

        void Body(PanelId id)
        {
            using (w.Panel($"body_{id}", _theme.Surface,
                UISize.Percent(1f), UISize.Percent(1f), Axis2.Y,
                padding: Sides.All(_theme.Pad), gap: _theme.Gap, clip: true))
            {
                // A panel is a rectangle somebody can drag down to nothing, so every panel body
                // scrolls rather than the ones somebody remembered to make scroll.
                ui.TopParent.Flags |= UIBoxFlags.AllowOverflowY | UIBoxFlags.ScrollY;

                switch (id)
                {
                    case PanelId.GpuTimings:
                        GpuTimingsPanel.Draw(w, stats, cost);
                        break;
                }
            }
        }

        // Closing a panel is the same thing as unticking it in the View menu, so it dispatches
        // the message the menu does and the model stays the one place a panel's visibility is
        // written down.
        void Requested(PanelRequest request)
        {
            if (request.Kind != PanelRequestKind.Close)
                return;

            if (request.Panel.Body is PanelBody.Leaf leaf)
                EditorLayout.PanelOf(leaf.View).IfSome(id =>
                    appMessages.Add(new AppUiMsg.SetPanelVisible(id, false)));
        }
    }

    private bool OverHole(UIContext ui)
    {
        foreach (UIBox hole in _holes)
            if (ui.ContainsMouse(hole))
                return true;

        return false;
    }
}
