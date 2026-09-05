using System.Collections.Immutable;
using Ptah;
using Ptah.Entities;
using Ptah.Functional;
using Ptah.Widgets;
using RenderLab.Assets;
using RenderLab.Project;
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
    /// <summary>
    /// The box the scene was drawn in, this frame. Two things read it: the mouse, because the
    /// viewport is the one region of the window the shell does not want the pointer, and the
    /// application, because the size of this box is the size the next frame has to be rendered at.
    /// <para>
    /// It is the box rather than its rectangle because a rectangle is only true after the layout
    /// has run, which is after the build this is filled in during. Reading
    /// <see cref="ViewportRect"/> once the frame is built gets this frame's answer.
    /// </para>
    /// </summary>
    private Optional<UIBox> _viewport = Optional<UIBox>.None;

    /// <summary>
    /// Where an open drop-down, colour picker, expanded node or half-typed dialog lives. See
    /// <see cref="WidgetState"/>.
    /// </summary>
    private readonly WidgetState _widgets = new();

    private PanelTree _tree;
    private int _layout;

    /// <summary>
    /// Where the scene goes, in logical pixels, as of the frame just built. None while no layout
    /// shows a viewport at all - every column full of panels, which the spec does not currently
    /// allow but the tree would happily describe.
    /// </summary>
    public Optional<Rect> ViewportRect => _viewport.Map(box => box.Rect);

    public EditorView()
    {
        // Starts holding a tree with no panels in it at all, so the first frame's live set is a
        // change like any other and there is one codepath that ever builds a tree.
        var hidden = AppUiModel.Default with { VisiblePanels = ImmutableHashSet<PanelId>.Empty };
        _layout = EditorLayout.LayoutMask(hidden);
        _tree = EditorLayout.Build(hidden);
    }

    /// <param name="scene">
    /// The picture the renderer drew last, which the viewport leaf shows. <c>ImageId.None</c>
    /// draws nothing, which is what the first frame has - the size to render at is not known until
    /// this build has said how big the viewport is.
    /// </param>
    public UiViewResult Draw(UIContext ui, AppUiModel app, UiModel model,
        IAssetCatalog catalog, AssetLibrary library, ProjectAssetIndex project,
        ImmutableArray<VisualizationMode> visualizations, FrameStats stats, UiCost cost,
        ImageId scene)
    {
        int layout = EditorLayout.LayoutMask(app);
        if (layout != _layout)
        {
            _layout = layout;
            _tree = EditorLayout.Build(app);
        }

        var appMessages = new List<AppUiMsg>();
        var messages = new List<UiMsg>();
        _viewport = Optional<UIBox>.None;

        // The palette is read off the model each frame rather than held, so switching it is one
        // more message the reducer folds and no state of the view's own. Everything below asks
        // the kit for a role, so the whole of a theme change is this line resolving differently.
        Theme theme = EditorTheme.Of(app.Theme);
        var w = new WidgetKit(ui, theme);

        // The bar owns the top strip and the panel area takes what is left, which is why the
        // shell is given the whole client area now: the `top` offset the frame used to be handed
        // existed only so two layouts drawn over each other agreed about where the workspace
        // began, and there is one layout again.
        //
        // It paints the ground. It used to paint nothing, because the shell was composited over a
        // frame the renderer had already drawn across the whole window and any background here
        // would have been a background over the scene. The scene is a picture inside a leaf now,
        // so the window is the shell's, and what is behind a column with no panels in it is this.
        using (w.Panel("shell", theme.Chrome, UISize.Percent(1f), UISize.Percent(1f), Axis2.Y))
        {
            EditorMenuBar.Draw(w, _widgets, app, appMessages.Add);
            w.Separator();

            // Close and nothing else on a header: this layout is authored in code, and a split
            // made with the mouse would have nowhere to be authored back to.
            w.PanelArea(_tree, EditorLayout.TitleOf, BuildLeaf,
                    Optional.Some<Func<ViewId, bool>>(EditorLayout.IsChromeless),
                    buttons: Optional.Some(PanelHeaderButtons.Close))
                .IfSome(Requested);
        }

        // Beside the panel area rather than inside it. A modal dims the whole window and takes
        // the mouse and the keyboard from everything behind it, which includes the panel that
        // opened it, so it is not that panel's to build.
        AssetDialogs.Draw(w, _widgets, library, appMessages.Add);

        // The frame's mutation phase, after the build, when nothing is walking the tree: a
        // boundary noticed during the build moves the next one.
        _tree.Commit();
        _tree.ApplyCommands();

        return new UiViewResult(appMessages, messages,
            new UiIntent(WantCaptureMouse: !OverViewport(ui), WantCaptureKeyboard: ui.TextInputFocused));

        void BuildLeaf(Panel panel, ViewId view)
        {
            if (view == EditorLayout.Viewport)
            {
                // The leaf rather than the picture inside it, because the leaf is the box that
                // takes the pointer - a picture is not clickable, and the camera is dragged in
                // here. They are the same rectangle: the image fills the leaf exactly, and a
                // margin between them would be a margin the renderer had rendered pixels for and
                // the shell then covered up.
                _viewport = Optional.Some(ui.TopParent);
                w.Image("viewport", scene, UISize.Percent(1f), UISize.Percent(1f));
                return;
            }

            // An emptied column shows the shell's ground, which the shell has already painted.
            if (view == EditorLayout.Empty)
                return;

            EditorLayout.PanelOf(view).IfSome(Body);
        }

        void Body(PanelId id)
        {
            using (w.Panel($"body_{id}", theme.Surface,
                UISize.Percent(1f), UISize.Percent(1f), Axis2.Y,
                padding: Sides.All(theme.Pad), gap: theme.Gap, clip: true))
            {
                // A panel is a rectangle somebody can drag down to nothing, so every panel body
                // scrolls rather than the ones somebody remembered to make scroll.
                ui.TopParent.Flags |= UIBoxFlags.AllowOverflowY | UIBoxFlags.ScrollY;

                switch (id)
                {
                    case PanelId.GpuTimings:
                        GpuTimingsPanel.Draw(w, stats, cost);
                        break;
                    case PanelId.Visualization:
                        VisualizationPanel.Draw(w, _widgets, model.Viz, visualizations,
                            messages.Add);
                        break;
                    case PanelId.Lighting:
                        LightingPanel.Draw(w, _widgets, model, messages.Add);
                        break;
                    case PanelId.Inspector:
                        InspectorPanel.Draw(w, _widgets, model, catalog, library,
                            messages.Add, appMessages.Add);
                        break;
                    case PanelId.Scene:
                        ScenePanel.Draw(w, _widgets, model, catalog, messages.Add);
                        break;
                    case PanelId.AssetBrowser:
                        AssetBrowserPanel.Draw(w, _widgets, model, library,
                            messages.Add, appMessages.Add);
                        break;
                    case PanelId.Project:
                        ProjectPanel.Draw(w, _widgets, project, appMessages.Add);
                        break;
                    case PanelId.RenderGraph:
                        RenderGraphPanel.Draw(w, _widgets, stats.ResolvedPasses);
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

    /// <summary>
    /// Whether the pointer is over the scene rather than over the interface. It is the whole of
    /// <see cref="UiIntent.WantCaptureMouse"/>, inverted: everywhere else in the window belongs to
    /// a panel, including a column emptied of them, so a drag started there is the editor's and not
    /// the camera's.
    /// </summary>
    private bool OverViewport(UIContext ui) =>
        _viewport.Match(some: ui.ContainsMouse, none: () => false);
}
