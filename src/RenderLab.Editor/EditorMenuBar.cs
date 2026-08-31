using Ptah.Widgets;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// The main menu bar: File and View, and the last interface Dear ImGui was drawing.
/// <para>
/// An entry is a label and a string this file chose, never a lambda and never a message. The bar
/// hands the string back when it is picked and knows nothing about what it meant, so
/// <see cref="Dispatch"/> is the one place in the editor that turns a menu into an
/// <see cref="AppUiMsg"/> - and the arguments an entry carries (which scene, which panel) ride in
/// the string exactly as they do in a command line.
/// </para>
/// <para>
/// Both menus are built from the model each frame rather than written out once. Every line of the
/// View menu is a tick over <c>AppUiModel.VisiblePanels</c>, half of File is greyed out until a
/// scene is open, and the scene list is whatever the project turned out to hold: none of that can
/// be a table, and a table would have had to be kept in step with the model by hand.
/// </para>
/// </summary>
internal static class EditorMenuBar
{
    /// <summary>The entry that opens a named scene. What follows the space is the path.</summary>
    private const string OpenScene = "scene.open ";

    /// <summary>The entry that shows or hides a panel. What follows the space is the panel.</summary>
    private const string ShowPanel = "panel.show ";

    internal static void Draw(WidgetKit w, WidgetState state, AppUiModel app,
        Action<AppUiMsg> dispatch) =>
        w.MenuBar(Menus(app), state.Menus).IfSome(id => Dispatch(id, app, dispatch));

    private static MenuSpec[] Menus(AppUiModel app) =>
        [new("File", FileMenu(app)), new("View", ViewMenu(app))];

    private static MenuEntry[] FileMenu(AppUiModel app)
    {
        bool hasScene = app.ActiveScenePath.Length > 0;

        // Three ASCII dots rather than an ellipsis, and no accented characters anywhere: the
        // atlas is printable ASCII, so a character outside it arrives on screen as a question
        // mark. The same rule the Inspector's headings were ported under.
        return
        [
            new MenuEntry("New Project...", "project.new"),
            new MenuEntry("Open Project...", "project.open"),
            MenuEntry.Separator,
            new MenuEntry("Open Scene", "scene.open")
            {
                Enabled = !app.AvailableScenes.IsEmpty,
                Submenu = SceneMenu(app),
            },
            new MenuEntry("Reload Scene", "scene.reload") { Enabled = hasScene },
            MenuEntry.Separator,
            new MenuEntry(SaveLabel(app), "scene.save") { Enabled = hasScene },
            new MenuEntry("Save Scene As...", "scene.saveas") { Enabled = hasScene },
            MenuEntry.Separator,
            new MenuEntry("Import glTF...", "gltf.import"),
            MenuEntry.Separator,

            // The one shortcut printed here that the tool actually has, because the window
            // manager provides it. See the plan for the one that was printed and did not exist.
            new MenuEntry("Exit", "app.exit") { Shortcut = "Alt+F4" },
        ];
    }

    /// <summary>
    /// The project's scenes, with the open one ticked. A submenu because it is one idea - open a
    /// scene - and however many scenes the project turned out to have.
    /// </summary>
    private static MenuEntry[] SceneMenu(AppUiModel app) =>
    [
        .. app.AvailableScenes.Select(path => new MenuEntry(path, OpenScene + path)
        {
            Checked = string.Equals(path, app.ActiveScenePath, StringComparison.OrdinalIgnoreCase),
        }),
    ];

    /// <summary>
    /// Which file a save would write, and whether it needs writing. The menu is where a dirty
    /// flag is worth showing: it is the thing you open when you are wondering.
    /// </summary>
    private static string SaveLabel(AppUiModel app) => app.ActiveScenePath.Length == 0
        ? "Save Scene"
        : $"Save Scene ({app.ActiveScenePath}){(app.SceneDirty ? "*" : string.Empty)}";

    /// <summary>
    /// One ticked line per panel, in the order the panels are declared. Enumerated rather than
    /// listed, so a panel added to <see cref="PanelId"/> arrives in this menu already - the hand
    /// written list this replaces was a second place every panel had to be remembered, and the
    /// names come from the layout, which is where a panel's title already lives.
    /// </summary>
    private static MenuEntry[] ViewMenu(AppUiModel app) =>
    [
        .. Enum.GetValues<PanelId>().Select(id => new MenuEntry(
            EditorLayout.TitleOf(EditorLayout.ViewOf(id)), ShowPanel + id)
        {
            Checked = app.IsPanelVisible(id),
        }),
    ];

    /// <summary>
    /// What an entry meant. The only place that knows, which is the whole reason the bar hands
    /// back a string: an entry that carried a message would put half the meaning in the table
    /// above and half in whoever folded it.
    /// </summary>
    private static void Dispatch(string id, AppUiModel app, Action<AppUiMsg> dispatch)
    {
        if (id.StartsWith(OpenScene, StringComparison.Ordinal))
        {
            dispatch(new AppUiMsg.RequestOpenScene(id[OpenScene.Length..]));
            return;
        }

        if (id.StartsWith(ShowPanel, StringComparison.Ordinal) &&
            Enum.TryParse(id[ShowPanel.Length..], out PanelId panel))
        {
            // A tick says what is showing, so picking one asks for the opposite of what it says.
            dispatch(new AppUiMsg.SetPanelVisible(panel, !app.IsPanelVisible(panel)));
            return;
        }

        switch (id)
        {
            case "project.new": dispatch(new AppUiMsg.RequestNewProjectDialog()); break;
            case "project.open": dispatch(new AppUiMsg.RequestOpenProjectDialog()); break;
            case "scene.reload": dispatch(new AppUiMsg.RequestReloadScene()); break;
            case "scene.save": dispatch(new AppUiMsg.RequestSaveScene()); break;
            case "scene.saveas": dispatch(new AppUiMsg.RequestSaveSceneAs()); break;
            case "gltf.import": dispatch(new AppUiMsg.RequestImportGltfDialog()); break;
            case "app.exit": dispatch(new AppUiMsg.RequestExit()); break;
        }
    }
}
