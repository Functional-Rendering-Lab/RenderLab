using Ptah;
using Ptah.Widgets;
using RenderLab.Project;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// The project's files as they sit on disk: folders, and what is in them. A pure view over
/// <see cref="ProjectAssetIndex"/> - the tree shape and the file classification both come from the
/// scan - which emits <see cref="AppUiMsg"/> for rescan, open-scene, import, and reveal.
/// <para>
/// Nothing here is selectable. The Asset Browser lists what the project has that the engine can
/// use, keyed by guid, and that is the list a selection is made from; this one is the filesystem,
/// and a file that is not an asset has nothing for the Inspector to show.
/// </para>
/// <para>
/// The kind glyphs are gone. Ptah's font atlas bakes printable ASCII, so the folder, film-clapper
/// and cube emoji the ImGui panel prefixed its rows with would each have arrived as a question
/// mark - and they were saying what the row already said: a folder is the node with an arrow on
/// it, and <c>sphere.glb</c> announces itself. Widening the atlas is the change icons want, and
/// it should be made for icons rather than to keep a decoration that was already redundant.
/// </para>
/// </summary>
internal static class ProjectPanel
{
    private const string OpenScene = "open";
    private const string Import = "import";
    private const string Reveal = "reveal";
    private const string CopyPath = "copy";

    /// <summary>What anything with a path offers, and the tail of every other menu here.</summary>
    private static readonly MenuEntry[] PathMenu =
    [
        new("Reveal in Explorer", Reveal),
        new("Copy project path", CopyPath),
    ];

    private static readonly MenuEntry[] SceneMenu = [new("Open Scene", OpenScene), .. PathMenu];

    private static readonly MenuEntry[] ModelMenu = [new("Import", Import), .. PathMenu];

    internal static void Draw(WidgetKit w, WidgetState state, ProjectAssetIndex index,
        Action<AppUiMsg> dispatchApp)
    {
        using (w.ButtonRow("project_actions"))
        {
            if (w.ToolButton("Refresh").Clicked)
                dispatchApp(new AppUiMsg.RequestRescanProject());

            using (w.Ui.Size(UISize.Text(), UISize.Text()))
            using (w.Ui.TextColor(w.Theme.Muted))
                w.DataRow("scanned", AssetBrowserPanel.Stamp(index.ScannedAtUtc));
        }

        w.Separator();

        if (string.IsNullOrEmpty(index.ProjectRoot))
        {
            using (w.Ui.TextColor(w.Theme.Muted))
                w.TextWrapped("noproject", "No project open.");

            return;
        }

        Children(w, state, index.Root, depth: 0, dispatchApp);
    }

    private static void Children(WidgetKit w, WidgetState state, ProjectFolderEntry folder,
        int depth, Action<AppUiMsg> dispatchApp)
    {
        foreach (ProjectFolderEntry sub in folder.Subfolders)
            Folder(w, state, sub, depth, dispatchApp);

        foreach (ProjectFileEntry file in folder.Files)
            File(w, state, file, dispatchApp);
    }

    private static void Folder(WidgetKit w, WidgetState state, ProjectFolderEntry folder,
        int depth, Action<AppUiMsg> dispatchApp)
    {
        // The path is the node's identity, so which folders are open survives a rescan that
        // renumbers everything else. The label is the name, which is data and may change.
        string key = $"dir_{folder.ProjectRelativePath}";
        TreeComm node = w.TreeNode(state.Trees, key, folder.Name, defaultOpen: depth == 0);

        w.ContextMenu(state.Popups, key, node.Comm, PathMenu).IfSome(picked =>
            OnPath(w, picked, folder.AbsolutePath, folder.ProjectRelativePath, dispatchApp));

        if (!node.Open)
            return;

        using (w.Indent())
            Children(w, state, folder, depth + 1, dispatchApp);
    }

    /// <summary>
    /// One file. A leaf node rather than a plain row, so its name starts where a folder's name
    /// does instead of underneath the arrow the folder beside it has and it does not.
    /// </summary>
    private static void File(WidgetKit w, WidgetState state, ProjectFileEntry file,
        Action<AppUiMsg> dispatchApp)
    {
        string key = $"file_{file.ProjectRelativePath}";
        TreeComm node = w.TreeNode(state.Trees, key, file.Name, leaf: true);

        if (node.Comm.DoubleClicked)
            Activate(file, dispatchApp);

        w.ContextMenu(state.Popups, key, node.Comm, MenuFor(file.Kind)).IfSome(picked =>
        {
            switch (picked)
            {
                case OpenScene:
                    dispatchApp(new AppUiMsg.RequestOpenScene(file.ProjectRelativePath));
                    break;
                case Import:
                    dispatchApp(new AppUiMsg.RequestImportGltf(file.AbsolutePath));
                    break;
                default:
                    OnPath(w, picked, file.AbsolutePath, file.ProjectRelativePath, dispatchApp);
                    break;
            }
        });
    }

    /// <summary>What a double click does, which is whatever the menu's first entry would have.</summary>
    private static void Activate(ProjectFileEntry file, Action<AppUiMsg> dispatchApp)
    {
        switch (file.Kind)
        {
            case ProjectFileKind.Scene:
                dispatchApp(new AppUiMsg.RequestOpenScene(file.ProjectRelativePath));
                break;
            case ProjectFileKind.GltfModel:
                dispatchApp(new AppUiMsg.RequestImportGltf(file.AbsolutePath));
                break;
        }
    }

    private static MenuEntry[] MenuFor(ProjectFileKind kind) => kind switch
    {
        ProjectFileKind.Scene => SceneMenu,
        ProjectFileKind.GltfModel => ModelMenu,
        _ => PathMenu,
    };

    private static void OnPath(WidgetKit w, string picked, string absolute, string relative,
        Action<AppUiMsg> dispatchApp)
    {
        switch (picked)
        {
            case Reveal:
                dispatchApp(new AppUiMsg.RequestRevealInExplorer(absolute));
                break;
            case CopyPath:
                w.Ui.Clipboard.Text = relative;
                break;
        }
    }
}
