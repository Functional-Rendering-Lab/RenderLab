using System.Collections.Immutable;
using System.Numerics;
using Ptah;
using Ptah.Functional;
using RenderLab.Assets;
using RenderLab.Graph;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Editor.Tests;

/// <summary>
/// One frame of the Ptah shell, built headlessly. A <c>UIContext</c> needs a text measurer and
/// nothing else - no window, no device, no atlas - so a whole frame of the editor can be built
/// and asserted on in a unit test, every branch of the Inspector can be visited without a GPU,
/// and a gesture can be driven through the outliners with no hand on the mouse.
/// <para>
/// Which is what these are for. Two properties are asked of every branch, and they are the two a
/// ported form gets wrong: a frame where nobody touched anything emits no message, and a
/// selection pointing at something that has been deleted still draws. The rest drive the gestures
/// the panels exist for - picking a row, opening a context menu, answering a dialog - because a
/// menu entry that dispatches the wrong message looks exactly like one that dispatches the right
/// one until somebody clicks it.
/// </para>
/// </summary>
public class EditorFrameTests
{
    private static readonly Guid DrawableId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MeshGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TextureGuid = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid MaterialGuid = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ProceduralGuid = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Missing = Guid.Parse("99999999-9999-9999-9999-999999999999");

    /// <summary>Every glyph 8px wide and every line 16 tall, so a frame is arithmetic.</summary>
    private static readonly Func<string, float, Vector2> Measure =
        static (text, _) => new Vector2(text.Length * 8f, 16f);

    // ---- What a frame is given --------------------------------------------------

    private static UiModel Model(Selection selection) => UiModel.Default with
    {
        Selection = selection,
        Drawables =
        [
            new EditableDrawable(DrawableId, "Sphere", new MeshId(1), Transform.Default, new MaterialId(1)),
        ],
    };

    private static AssetLibrary Library() => AssetLibrary.Empty with
    {
        ScannedAtUtc = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc),
        ByGuid = ImmutableDictionary<Guid, AssetEntry>.Empty
            .Add(MeshGuid, new FileAssetEntry(
                MeshGuid, AssetKind.Mesh, "teapot", "assets/teapot.glb", new MeshImportSettings(2f)))
            .Add(TextureGuid, new FileAssetEntry(
                TextureGuid, AssetKind.Texture, "brick", "assets/brick.png", new TextureImportSettings()))
            .Add(MaterialGuid, new MaterialAssetEntry(
                MaterialGuid, "shiny", "assets/materials/shiny.mat.json",
                new MaterialParamsDoc([0.6f, 0.5f, 0.4f], 0.5f, 32f),
                RenderLab.Functional.Optional.Some(new AssetRef(TextureGuid))))
            .Add(ProceduralGuid, new ProceduralAssetEntry(
                ProceduralGuid, AssetKind.Mesh, "unit quad", "assets/proc/quad.proc.meta",
                "quad", Params: null)),
    };

    /// <summary>Two folders with one file each, which is enough shape for a tree to be a tree.</summary>
    private static ProjectAssetIndex Project()
    {
        var scene = new ProjectFileEntry(
            "demo.scene", "scenes/demo.scene", @"C:\proj\scenes\demo.scene",
            ProjectFileKind.Scene, 128L, DateTime.UnixEpoch);

        var model = new ProjectFileEntry(
            "teapot.glb", "models/teapot.glb", @"C:\proj\models\teapot.glb",
            ProjectFileKind.GltfModel, 4096L, DateTime.UnixEpoch);

        var scenes = new ProjectFolderEntry(
            "scenes", "scenes", @"C:\proj\scenes", [], [scene]);

        var models = new ProjectFolderEntry(
            "models", "models", @"C:\proj\models", [], [model]);

        var root = new ProjectFolderEntry("", "", @"C:\proj", [scenes, models], []);

        return new ProjectAssetIndex(@"C:\proj", root, new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>
    /// Two passes with a barrier between them, which is the smallest thing that is a graph: one
    /// writes what the other reads, and the compiler had to notice.
    /// </summary>
    private static ImmutableArray<ResolvedPass> Passes()
    {
        ResourceName albedo = ResourceName.Of("gAlbedo");

        var gbuffer = new RenderPassDeclaration(
            "GBuffer", [], [new PassOutput(albedo, ResourceUsage.ColorAttachmentWrite)]);

        var lighting = new RenderPassDeclaration(
            "Lighting", [new PassInput(albedo, ResourceUsage.ShaderRead)], []);

        return
        [
            new ResolvedPass(gbuffer, []),
            new ResolvedPass(lighting,
                [new BarrierDesc(albedo, ResourceUsage.ColorAttachmentWrite, ResourceUsage.ShaderRead)]),
        ];
    }

    private static readonly FrameStats Stats = new(
        DeltaSeconds: 1f / 60f,
        TimestampLabels: ["GBuffer"],
        TimestampMillis: [0.42d],
        ResolvedPasses: Passes());

    // ---- Driving one -------------------------------------------------------------

    /// <summary>
    /// An editor being used: the view, its context, and the model handed to it every frame.
    /// <para>
    /// It is a driver rather than a helper because a gesture is more than one frame. A click is a
    /// press and a release; a menu is a right-press, then the frame that lays the menu out, then
    /// a click on an entry - and each of those reads a rectangle the frame before it solved.
    /// </para>
    /// </summary>
    private sealed class Driver
    {
        private readonly EditorView _view = new();
        private readonly FakeCatalog _catalog = new();

        internal readonly UIContext Ui = new(Measure, EditorTheme.FontSize);

        internal UiModel Model = EditorFrameTests.Model(new Selection.None());
        internal AppUiModel App = AppUiModel.Default;
        internal AssetLibrary Library = EditorFrameTests.Library();
        internal ProjectAssetIndex Project = EditorFrameTests.Project();

        /// <summary>
        /// What the hosting pipeline can resolve to the screen, which is what the Visualization
        /// panel offers. All of them here, because a test that could not reach a mode would be
        /// testing the fixture.
        /// </summary>
        internal ImmutableArray<VisualizationMode> Visualizations =
            [.. Enum.GetValues<VisualizationMode>()];

        internal FrameStats Stats = EditorFrameTests.Stats;

        /// <summary>
        /// The picture the viewport leaf draws. A registered id is only a number, so a fixture
        /// with no backend behind it can still say the scene is there - which is what makes the
        /// difference between the viewport drawing an image and drawing nothing testable.
        /// </summary>
        internal ImageId Scene = new(1);

        /// <summary>Where the viewport came out, as the application reads it after a frame.</summary>
        internal Optional<Rect> ViewportRect => _view.ViewportRect;

        internal UiViewResult Step(UIInput input)
        {
            Ui.BeginBuild(1f / 60f, new Rect(0f, 0f, 1600f, 900f), input);
            UiViewResult result = _view.Draw(Ui, App, Model, _catalog, Library, Project,
                Visualizations, Stats, default, Scene);
            Ui.EndBuild();
            return result;
        }

        /// <summary>
        /// Two frames, and the second one's result. Two, because anything measured against a
        /// solved rectangle - a wrapped paragraph, a popup anchored under its button - has last
        /// frame's layout to read on the second and nothing on the first.
        /// </summary>
        internal UiViewResult Settle()
        {
            Step(default);
            return Step(default);
        }

        /// <summary>A press and a release on whatever reads <paramref name="label"/>.</summary>
        internal UiViewResult Click(string label)
        {
            Vector2 at = Box(label).Rect.Center;
            Step(new UIInput { MousePosition = at, MouseDown = true, MousePressed = true });
            return Step(new UIInput { MousePosition = at, MouseReleased = true });
        }

        /// <summary>
        /// The pointer resting on something, pressing nothing. It is a gesture in its own right
        /// once a menu is open: a branch opens because the pointer stopped on its parent.
        /// </summary>
        internal UiViewResult Hover(string label) =>
            Step(new UIInput { MousePosition = Box(label).Rect.Center });

        /// <summary>The second mouse button, which is what opens a context menu.</summary>
        internal UiViewResult RightClick(string label)
        {
            Vector2 at = Box(label).Rect.Center;
            return Step(new UIInput { MousePosition = at, SecondaryDown = true, SecondaryPressed = true });
        }

        /// <summary>
        /// Every box in the frame, and every layer of it. Menus and dialogs are built on the
        /// dropdown layer, which is a root of its own - so a walk from <c>Ui.Root</c> is a walk
        /// of the application and never of what is over it.
        /// </summary>
        internal IEnumerable<UIBox> Boxes() =>
            Enum.GetValues<UILayer>().SelectMany(layer => Walk(Ui.RootOf(layer)));

        /// <summary>
        /// The innermost box that can be pressed and reads <paramref name="label"/> somewhere
        /// inside it. Innermost, because the walk is pre-order and a row inside a clickable
        /// region would otherwise be answered for by the region.
        /// </summary>
        internal UIBox Box(string label) => Boxes()
            .Where(box => box.Flags.Has(UIBoxFlags.Clickable) &&
                          Walk(box).Any(inner => inner.DisplayString == label))
            .Last();

        internal bool Shows(string label) => Boxes().Any(box => box.DisplayString == label);

        /// <summary>
        /// Whether anything the user could press reads this. A control built but disabled shows
        /// and does not answer, which is the difference this asks about.
        /// </summary>
        internal bool CanPress(string label) => Boxes().Any(box =>
            box.Flags.Has(UIBoxFlags.Clickable) &&
            Walk(box).Any(inner => inner.DisplayString == label));

        /// <summary>Leaves one panel on screen, so a label can only mean one thing.</summary>
        internal Driver Only(PanelId id)
        {
            foreach (PanelId other in Enum.GetValues<PanelId>())
            {
                if (other != id)
                    App = App.WithPanelVisible(other, false);
            }

            return this;
        }
    }

    private static IEnumerable<UIBox> Walk(UIBox box)
    {
        yield return box;
        foreach (UIBox child in box.Children)
        {
            foreach (UIBox nested in Walk(child))
                yield return nested;
        }
    }


    // ---- The menu bar ------------------------------------------------------------
    //
    // The last interface Dear ImGui was drawing, and the one place in the editor where what a
    // control means is a string rather than a call. That is exactly the thing a frame cannot be
    // read for: an entry that dispatches the wrong message looks like one that dispatches the
    // right one until somebody picks it.

    private static Driver WithProject(Driver driver, string active, params string[] scenes)
    {
        driver.App = driver.App.WithProject("demo", active, [.. scenes]);
        return driver;
    }

    [Fact]
    public void TheMenuBarPicksUpWhatAnEntryMeans()
    {
        var driver = new Driver();
        driver.Settle();

        driver.Click("File");
        UiViewResult result = driver.Click("Exit");

        Assert.Contains(result.AppMessages, msg => msg is AppUiMsg.RequestExit);
    }

    [Fact]
    public void OpeningASceneCarriesThePathTheSubmenuNamed()
    {
        Driver driver = WithProject(new Driver(), "scenes/one.scene", "scenes/one.scene", "scenes/two.scene");
        driver.Settle();

        driver.Click("File");
        driver.Hover("Open Scene");

        // The submenu is the project's scenes, and the one already open is ticked.
        Assert.True(driver.Shows("scenes/two.scene"));

        UiViewResult result = driver.Click("scenes/two.scene");

        AppUiMsg.RequestOpenScene opened = Assert.IsType<AppUiMsg.RequestOpenScene>(
            Assert.Single(result.AppMessages));
        Assert.Equal("scenes/two.scene", opened.ProjectRelative);
    }

    [Fact]
    public void TheViewMenuAsksForTheOppositeOfWhatItsTickSays()
    {
        var driver = new Driver();
        driver.Settle();

        driver.Click("View");
        UiViewResult result = driver.Click("Scene");

        AppUiMsg.SetPanelVisible set = Assert.IsType<AppUiMsg.SetPanelVisible>(
            Assert.Single(result.AppMessages));
        Assert.Equal(PanelId.Scene, set.Id);
        Assert.False(set.Visible);
    }

    [Fact]
    public void TheViewMenuAsksForTheThemeItIsNotWearing()
    {
        var driver = new Driver();
        driver.Settle();

        driver.Click("View");
        UiViewResult result = driver.Click("Neutral Theme");

        Assert.IsType<AppUiMsg.ToggleTheme>(Assert.Single(result.AppMessages));
    }

    /// <summary>
    /// The other half of the toggle, and the half a dispatch test cannot see: the message only
    /// means something if the next frame is actually built in the palette it asked for. Nothing
    /// in the shell holds a theme, so this is the whole of the change - the model names one and
    /// every fill in the frame comes from it.
    /// </summary>
    [Fact]
    public void AFrameIsPaintedInTheThemeTheModelNames()
    {
        var driver = new Driver();
        driver.Settle();

        Assert.Contains(Theme.Warm.Surface, Fills(driver));

        driver.App = driver.App.WithThemeToggled();
        driver.Settle();

        Assert.Contains(Theme.Neutral.Surface, Fills(driver));
        Assert.DoesNotContain(Theme.Warm.Surface, Fills(driver));
    }

    /// <summary>Every fill in the frame, whatever layer it was drawn on.</summary>
    private static IEnumerable<Color> Fills(Driver driver) =>
        driver.Boxes().Select(box => box.BackgroundColors.TopLeft);

    [Fact]
    public void WithNoSceneOpenTheEntriesThatNeedOneDoNothing()
    {
        var driver = new Driver();
        driver.Settle();

        driver.Click("File");
        UiViewResult result = driver.Click("Reload Scene");

        // It is drawn, and it is inert. A menu whose lines come and go is one nobody can learn
        // the shape of, so a line that cannot be used stays where it is and answers nothing.
        Assert.True(driver.Shows("Reload Scene"));
        Assert.Empty(result.AppMessages);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void ASavedSceneNamesItsFileAndSaysWhenItIsDirty()
    {
        Driver driver = WithProject(new Driver(), "scenes/one.scene", "scenes/one.scene");
        driver.Settle();
        driver.Click("File");
        Assert.True(driver.Shows("Save Scene (scenes/one.scene)"));

        driver.App = driver.App with { SceneDirty = true };
        driver.Step(default);

        Assert.True(driver.Shows("Save Scene (scenes/one.scene)*"));
    }

    // ---- The Render Graph panel --------------------------------------------------

    [Fact]
    public void TheRenderGraphNamesEachPassInOrderWithWhatItReadsAndWrites()
    {
        var driver = new Driver().Only(PanelId.RenderGraph);
        driver.Settle();

        // The index is in the label because the order is the whole output of a graph compiler.
        Assert.True(driver.Shows("0: GBuffer"));
        Assert.True(driver.Shows("1: Lighting"));

        Assert.True(driver.Shows("gAlbedo (ColorAttachmentWrite)"));
        Assert.True(driver.Shows("gAlbedo (ShaderRead)"));

        // And the barrier the compiler inserted between the two, which is the part of a render
        // graph that is worth having a panel for.
        Assert.True(driver.Shows("gAlbedo ColorAttachmentWrite → ShaderRead"));
    }

    [Fact]
    public void APipelineWithNoGraphSaysSoRatherThanDrawingAnEmptyPanel()
    {
        var driver = new Driver().Only(PanelId.RenderGraph);
        driver.Stats = Stats with { ResolvedPasses = [] };
        driver.Settle();

        Assert.True(driver.Shows("No graph. This pipeline records its passes by hand."));
    }

    // ---- Every branch of the Inspector -------------------------------------------

    public static TheoryData<Selection> EverySelection() =>
    [
        new Selection.None(),
        new Selection.Drawable(DrawableId),
        new Selection.Light(0),
        new Selection.Light(3),
        new Selection.MaterialAsset(MaterialGuid),
        new Selection.MeshAsset(MeshGuid),
        new Selection.MeshAsset(ProceduralGuid),
        new Selection.TextureAsset(TextureGuid),
        new Selection.Environment(),
        new Selection.Camera(),
    ];

    [Theory]
    [MemberData(nameof(EverySelection))]
    public void AFrameThatChangesNothingEmitsNothing(Selection selection)
    {
        // The whole contract of a value-in, value-out form: with no input at all, every field
        // hands back what it was given, so nothing reports a change and no message is pushed.
        // A form that failed this would mark the scene dirty sixty times a second.
        var editor = new Driver { Model = Model(selection) };
        UiViewResult result = editor.Settle();

        Assert.Empty(result.Messages);
        Assert.Empty(result.AppMessages);
    }

    public static TheoryData<Selection> EveryDanglingSelection() =>
    [
        new Selection.Drawable(Missing),
        new Selection.Light(-1),
        new Selection.Light(99),
        new Selection.MaterialAsset(Missing),
        new Selection.MeshAsset(Missing),
        new Selection.TextureAsset(Missing),
    ];

    [Theory]
    [MemberData(nameof(EveryDanglingSelection))]
    public void ASelectionPointingAtSomethingGoneDrawsAFrameAnyway(Selection selection)
    {
        // A scene reload wipes the registry and hands back fresh ids, so between the reload and
        // the message that clears the selection there is at least one frame where the Inspector
        // is pointed at something that is not there. It says so; it does not throw.
        var editor = new Driver { Model = Model(selection) };
        UiViewResult result = editor.Settle();

        Assert.Empty(result.Messages);
        Assert.Empty(result.AppMessages);
    }

    // ---- The shell around them ---------------------------------------------------

    [Fact]
    public void TheSceneIsDrawnInTheViewportAtTheRectangleTheApplicationRendersTo()
    {
        var editor = new Driver();
        editor.Settle();

        UIBox picture = Assert.Single(editor.Boxes(), box => !box.Image.IsNone);
        Assert.Equal(editor.Scene, picture.Image);

        // The same rectangle, exactly. What the application renders into is what it reads back
        // here, so a picture inset inside its leaf would be a scene rendered at one size and
        // shown at another - which is the bug the whole arrangement exists to make impossible.
        Rect viewport = editor.ViewportRect.ValueOr(default(Rect));
        Assert.Equal(viewport, picture.Rect);
        Assert.True(viewport.Width > 0f && viewport.Height > 0f);
    }

    [Fact]
    public void AColumnEmptiedAwayFromTheViewportShowsNoSceneOfItsOwn()
    {
        // The left column, emptied, with the one beside it still full. It has nothing to draw and
        // it does not reach the viewport, so it is the shell's ground: a second picture here
        // would be a second copy of the scene, at a second aspect ratio.
        var editor = new Driver();
        foreach (PanelId id in new[]
                 { PanelId.Visualization, PanelId.Lighting, PanelId.Scene, PanelId.Project })
            editor.App = editor.App.WithPanelVisible(id, false);

        editor.Settle();

        Assert.Single(editor.Boxes(), box => !box.Image.IsNone);
    }

    [Fact]
    public void TheViewportReportsItsRectangleBeforeThereIsAnyPictureToShowInIt()
    {
        // The editor's first frame. Nothing has been rendered yet and nothing can have been: the
        // size to render at is what this frame is being built to find out.
        var editor = new Driver { Scene = ImageId.None };
        editor.Settle();

        Assert.DoesNotContain(editor.Boxes(), box => !box.Image.IsNone);
        Assert.True(editor.ViewportRect.Match(some: rect => rect.Width > 0f, none: () => false));
    }

    [Fact]
    public void ThePointerOverAPanelBelongsToTheShellAndOverTheViewportDoesNot()
    {
        var editor = new Driver { Model = Model(new Selection.Camera()) };

        // The Inspector's column is the second of four, so a point inside it is over a panel and
        // a point far to the right is over the viewport - the one region of the window where a
        // drag is the camera's rather than the interface's.
        UiIntent Over(Vector2 at)
        {
            UiViewResult result = default!;
            for (int i = 0; i < 2; i++)
                result = editor.Step(new UIInput { MousePosition = at });

            return result.Intent;
        }

        Assert.True(Over(new Vector2(280f, 400f)).WantCaptureMouse);
        Assert.False(Over(new Vector2(900f, 400f)).WantCaptureMouse);
    }

    [Fact]
    public void TickingACheckBoxAsksTheModelToChangeAndOnlyOnce()
    {
        // The whole path a port has to get right, driven the way a hand drives it: a press and a
        // release on the box the layout put there, one message out of it, and nothing further on
        // the frames afterwards.
        var editor = new Driver().Only(PanelId.Lighting);
        editor.Settle();

        // Two square buttons are built: the header's Close, and then the check box in the body.
        // The body is built after the header, so the check box is the second of them.
        UIBox check = Walk(editor.Ui.Root)
            .Where(box => box.Flags.Has(UIBoxFlags.Clickable) &&
                          Math.Abs(box.Rect.Width - box.Rect.Height) < 1f)
            .Last();

        Vector2 at = check.Rect.Center;
        Assert.Empty(editor.Step(new UIInput
        {
            MousePosition = at,
            MouseDown = true,
            MousePressed = true,
        }).Messages);

        UiViewResult released = editor.Step(new UIInput { MousePosition = at, MouseReleased = true });
        UiMsg.SetLightingOnly ticked = Assert.IsType<UiMsg.SetLightingOnly>(Assert.Single(released.Messages));
        Assert.True(ticked.On);

        // The model this test holds never folds that message, so a widget that reported a change
        // on every frame it was hovered would say so again here. It does not.
        Assert.Empty(editor.Step(new UIInput { MousePosition = at }).Messages);
    }

    // ---- The scene outliner ------------------------------------------------------

    [Fact]
    public void ClickingASceneRowSelectsItAndNothingElseHappens()
    {
        var editor = new Driver().Only(PanelId.Scene);
        editor.Settle();

        UiViewResult picked = editor.Click("Sphere");

        UiMsg.Select select = Assert.IsType<UiMsg.Select>(Assert.Single(picked.Messages));
        Selection.Drawable drawable = Assert.IsType<Selection.Drawable>(select.Selection);
        Assert.Equal(DrawableId, drawable.LocalId);
        Assert.Empty(picked.AppMessages);
    }

    [Fact]
    public void CloningIsUnavailableUntilSomethingIsSelectedAndThenNudgesTheCopy()
    {
        var editor = new Driver().Only(PanelId.Scene);
        editor.Settle();

        // Nothing selected: the button is built, so it can be seen, and it takes no press at
        // all - a disabled widget in Ptah gives its flags up rather than ignoring a click.
        Assert.True(editor.Shows("+ clone"));
        Assert.False(editor.CanPress("+ clone"));

        editor.Model = Model(new Selection.Drawable(DrawableId));
        editor.Settle();
        Assert.True(editor.CanPress("+ clone"));

        UiMsg.AddDrawable added = Assert.IsType<UiMsg.AddDrawable>(
            Assert.Single(editor.Click("+ clone").Messages));

        Assert.Equal("Sphere copy", added.Name);
        Assert.Equal(new Vector3(1f, 0f, 0f), added.Transform.Position);
    }

    [Fact]
    public void TheLightRowsSayWhichKindEachLightIsBecauseNothingElseWould()
    {
        var editor = new Driver
        {
            Model = UiModel.Default with
            {
                Lights =
                [
                    new PointLight(Vector3.Zero, Color01.UnsafeFrom(Vector3.One), Intensity.UnsafeFrom(1f)),
                    new DirectionalLight(
                        Direction.Create(new Vector3(0f, -1f, 0f)).Match(ok => ok, error: _ => throw new InvalidOperationException()),
                        Color01.UnsafeFrom(Vector3.One),
                        Intensity.UnsafeFrom(1f)),
                ],
            },
        }.Only(PanelId.Scene);

        editor.Settle();

        Assert.True(editor.Shows("[0] point"));
        Assert.True(editor.Shows("[1] directional"));

        UiMsg.Select select = Assert.IsType<UiMsg.Select>(Assert.Single(editor.Click("[1] directional").Messages));
        Assert.Equal(1, Assert.IsType<Selection.Light>(select.Selection).Index);
    }

    // ---- The asset browser, and the gesture that replaced the drag ----------------

    [Fact]
    public void AddToSceneOnAMeshAsksForTheSameThingTheDropUsedTo()
    {
        var editor = new Driver().Only(PanelId.AssetBrowser);
        editor.Settle();

        // The right-press that used to be the start of a drag now opens the menu the drop's
        // message moved onto.
        editor.RightClick("teapot");
        UiViewResult added = editor.Click("Add to Scene");

        AppUiMsg.RequestAddDrawableFromAsset request =
            Assert.IsType<AppUiMsg.RequestAddDrawableFromAsset>(Assert.Single(added.AppMessages));

        Assert.Equal(MeshGuid, request.MeshGuid);
    }

    [Fact]
    public void OnlyAMeshIsOfferedToTheScene()
    {
        var editor = new Driver().Only(PanelId.AssetBrowser);
        editor.Settle();

        editor.RightClick("brick");

        Assert.True(editor.Shows("Rename"));
        Assert.False(editor.Shows("Add to Scene"));
    }

    [Fact]
    public void RenameAsksBeforeItActsAndThenCarriesWhatWasTyped()
    {
        var editor = new Driver().Only(PanelId.AssetBrowser);
        editor.Settle();

        editor.RightClick("teapot");

        // Picking the entry opens a dialog rather than renaming anything.
        UiViewResult opened = editor.Click("Rename");
        Assert.Empty(opened.AppMessages);
        Assert.True(editor.Shows("Rename teapot"));

        // The caret is already in the field, so this is what a user would type next.
        editor.Step(new UIInput { Keys = [new UIKeyEvent(UIKeyCode.A, UIModifiers.Ctrl)] });
        editor.Step(new UIInput { TypedChars = "kettle".ToImmutableArray() });

        UiViewResult renamed = editor.Step(new UIInput { Keys = [new UIKeyEvent(UIKeyCode.Enter)] });

        AppUiMsg.RequestRenameAsset request =
            Assert.IsType<AppUiMsg.RequestRenameAsset>(Assert.Single(renamed.AppMessages));

        Assert.Equal(MeshGuid, request.Guid);
        Assert.Equal("kettle", request.NewName);

        // And the dialog is gone, rather than asking the same question every frame after.
        Assert.Empty(editor.Settle().AppMessages);
        Assert.False(editor.Shows("Rename teapot"));
    }

    [Fact]
    public void CancellingADialogDispatchesNothing()
    {
        var editor = new Driver().Only(PanelId.AssetBrowser);
        editor.Settle();

        editor.RightClick("brick");
        editor.Click("Delete");
        Assert.True(editor.Shows("Delete brick?"));

        UiViewResult cancelled = editor.Click("Cancel");
        Assert.Empty(cancelled.AppMessages);

        // The frame that answered the dialog is the frame that was still drawing it; the next
        // one is the first that is not.
        Assert.Empty(editor.Settle().AppMessages);
        Assert.False(editor.Shows("Delete brick?"));
    }

    [Fact]
    public void ADialogWhoseAssetHasGoneClosesItselfRatherThanAskingAboutNothing()
    {
        var editor = new Driver().Only(PanelId.AssetBrowser);
        editor.Settle();

        editor.RightClick("brick");
        editor.Click("Delete");
        Assert.True(editor.Shows("Delete brick?"));

        // The delete goes through, the library is rescanned, and the entry the dialog was asking
        // about is not in the new one. That is the ordinary path, not an edge case.
        editor.Library = editor.Library with { ByGuid = editor.Library.ByGuid.Remove(TextureGuid) };

        Assert.Empty(editor.Settle().AppMessages);
        Assert.False(editor.Shows("Delete brick?"));
    }

    // ---- The project tree ---------------------------------------------------------

    [Fact]
    public void ADoubleClickOnASceneFileAsksToOpenIt()
    {
        var editor = new Driver().Only(PanelId.Project);
        editor.Settle();

        Assert.Empty(editor.Click("demo.scene").AppMessages);
        UiViewResult opened = editor.Click("demo.scene");

        AppUiMsg.RequestOpenScene request =
            Assert.IsType<AppUiMsg.RequestOpenScene>(Assert.Single(opened.AppMessages));

        Assert.Equal("scenes/demo.scene", request.ProjectRelative);
    }

    [Fact]
    public void AFilesMenuOffersOnlyWhatCanBeDoneWithThatKindOfFile()
    {
        var editor = new Driver().Only(PanelId.Project);
        editor.Settle();

        editor.RightClick("demo.scene");
        Assert.True(editor.Shows("Open Scene"));
        Assert.False(editor.Shows("Import"));

        editor.Click("Open Scene");
        editor.Settle();

        editor.RightClick("teapot.glb");
        Assert.True(editor.Shows("Import"));
        Assert.False(editor.Shows("Open Scene"));
    }

    [Fact]
    public void RefreshAsksForARescanAndTheStampSaysWhenTheLastOneWas()
    {
        var editor = new Driver().Only(PanelId.Project);
        editor.Settle();

        Assert.IsType<AppUiMsg.RequestRescanProject>(Assert.Single(editor.Click("Refresh").AppMessages));

        editor.Project = ProjectAssetIndex.Empty(@"C:\proj");
        editor.Settle();

        Assert.True(editor.Shows("not scanned"));
    }

    /// <summary>
    /// A catalog holding one mesh and one material, which is all the Inspector's two combos and
    /// the Scene panel's picker read. The rest of the interface is a compile-time obligation
    /// rather than one this exercises.
    /// </summary>
    private sealed class FakeCatalog : IAssetCatalog
    {
        private static readonly MeshAsset Mesh = new(new MeshId(1), "unit sphere", new MeshData([], []));

        private static readonly MaterialAsset Material =
            BlinnPhongMaterial.Default(new MaterialId(1), "default");

        public IEnumerable<MeshAsset> AllMeshes => [Mesh];
        public IEnumerable<TextureAsset> AllTextures => [];
        public IEnumerable<MaterialAsset> AllMaterials => [Material];

        public MeshAsset GetMesh(MeshId id) => Mesh;
        public MaterialAsset GetMaterial(MaterialId id) => Material;
        public TextureAsset GetTexture(TextureId id) => throw new NotSupportedException();

        public bool TryGetMesh(MeshId id, out MeshAsset asset)
        {
            asset = Mesh;
            return id == Mesh.Id;
        }

        public bool TryGetMaterial(MaterialId id, out MaterialAsset asset)
        {
            asset = Material;
            return id == Material.Id;
        }

        public bool TryGetTexture(TextureId id, out TextureAsset asset)
        {
            asset = null!;
            return false;
        }

        public bool IsBuiltin(MeshId id) => false;
        public bool IsBuiltin(TextureId id) => false;
        public bool IsBuiltin(MaterialId id) => false;
    }
}
