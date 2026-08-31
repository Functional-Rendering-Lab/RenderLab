using Ptah;
using Ptah.Widgets;
using RenderLab.Project;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// The project's usable assets, grouped by kind. Clicking a row emits <see cref="UiMsg.Select"/>
/// and the Inspector draws the editor for it. Rename and Delete are on the row's context menu
/// because they are operations on the library rather than edits to an asset's properties, and
/// "Add to Scene" is there with them because a mesh in the project is not in the scene until
/// somebody puts it there.
/// <para>
/// That last entry is what replaced the drag. The Asset Browser was the tool's only drag source
/// and the Scene panel its only drop target, and between them they were the reason this migration
/// budgeted a payload that survives across frames plus drop-target hit testing - two new concepts
/// in Ptah, for one gesture. The message is the same one the drop dispatched, so nothing
/// downstream of the panel can tell the difference.
/// </para>
/// <para>
/// The rows carry the asset's name and no second column. The ImGui version put the project path
/// beside it, which in a column this narrow was a path clipped a third of the way through - and
/// the Inspector shows the whole of it for whatever is selected, which is the panel that exists
/// to say what a thing is.
/// </para>
/// </summary>
internal static class AssetBrowserPanel
{
    private const string AddToScene = "add";
    private const string Rename = "rename";
    private const string Delete = "delete";

    /// <summary>What a mesh offers. Only a mesh can be added to the scene as a drawable.</summary>
    private static readonly MenuEntry[] MeshMenu =
    [
        new("Add to Scene", AddToScene),
        new("Rename", Rename),
        new("Delete", Delete),
    ];

    private static readonly MenuEntry[] AssetMenu =
    [
        new("Rename", Rename),
        new("Delete", Delete),
    ];

    internal static void Draw(WidgetKit w, WidgetState state, UiModel model, AssetLibrary library,
        Action<UiMsg> dispatch, Action<AppUiMsg> dispatchApp)
    {
        // Captured before anything is built, so it is the panel's own body: the region the
        // keyboard shortcuts below ask about.
        UIBox body = w.Ui.TopParent;

        using (w.Ui.TextColor(w.Theme.Muted))
            w.DataRow("scanned", Stamp(library.ScannedAtUtc));

        w.Separator();

        Section(w, state, model, library, AssetKind.Mesh, "Meshes", dispatch, dispatchApp);
        Section(w, state, model, library, AssetKind.Texture, "Textures", dispatch, dispatchApp);
        Section(w, state, model, library, AssetKind.Material, "Materials", dispatch, dispatchApp);

        Shortcuts(w, state, model, library, body);
    }

    internal static string Stamp(DateTime scannedAtUtc) => scannedAtUtc == DateTime.MinValue
        ? "not scanned"
        : $"scanned {scannedAtUtc.ToLocalTime():HH:mm:ss}";

    private static void Section(WidgetKit w, WidgetState state, UiModel model,
        AssetLibrary library, AssetKind kind, string label,
        Action<UiMsg> dispatch, Action<AppUiMsg> dispatchApp)
    {
        AssetEntry[] entries =
        [
            .. library.EntriesOfKind(kind).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
        ];

        TreeComm node = w.TreeNode(state.Trees, $"assets_{kind}", $"{label} ({entries.Length})",
            defaultOpen: true);

        if (!node.Open)
            return;

        using (w.Indent())
        {
            foreach (AssetEntry entry in entries)
                Row(w, state, model, entry, dispatch, dispatchApp);
        }
    }

    private static void Row(WidgetKit w, WidgetState state, UiModel model, AssetEntry entry,
        Action<UiMsg> dispatch, Action<AppUiMsg> dispatchApp)
    {
        string key = $"asset_{entry.Guid:N}";
        TreeComm row = w.TreeNode(state.Trees, key, entry.Name,
            selected: IsSelected(model.Selection, entry), leaf: true);

        if (row.Comm.Clicked)
            dispatch(new UiMsg.Select(SelectionFor(entry)));

        // Written next to the row it belongs to, and opened by the row's own right-press. What
        // the entry means stays here rather than in the menu.
        w.ContextMenu(state.Popups, key, row.Comm,
                entry.Kind == AssetKind.Mesh ? MeshMenu : AssetMenu)
            .IfSome(picked => Act(state, entry, picked, dispatchApp));
    }

    private static void Act(WidgetState state, AssetEntry entry, string picked,
        Action<AppUiMsg> dispatchApp)
    {
        switch (picked)
        {
            case AddToScene:
                dispatchApp(new AppUiMsg.RequestAddDrawableFromAsset(entry.Guid));
                break;
            case Rename:
                AssetDialogs.Open(state, AssetDialogKind.Rename, entry);
                break;
            case Delete:
                AssetDialogs.Open(state, AssetDialogKind.Delete, entry);
                break;
        }
    }

    /// <summary>
    /// F2 and Delete, on the selected asset, while the pointer is in this panel.
    /// <para>
    /// The ImGui version asked whether the Asset Browser's window was focused. Ptah panels are
    /// regions of one window and never take focus, so the honest equivalent is where the pointer
    /// is - and it is the stricter rule of the two, because a focused ImGui window kept the
    /// shortcut while the cursor was somewhere else entirely. The pointer has to be on something
    /// in the panel rather than merely inside its rectangle, which is what
    /// <c>UIContext.ContainsMouse</c> answers and is why an open dialog cannot reach this: its
    /// scrim wins the hit test over everything behind it.
    /// </para>
    /// </summary>
    private static void Shortcuts(WidgetKit w, WidgetState state, UiModel model,
        AssetLibrary library, UIBox body)
    {
        if (state.Dialog is not null || !w.Ui.ContainsMouse(body))
            return;

        if (Selected(model.Selection, library) is not AssetEntry entry)
            return;

        foreach (UIKeyEvent key in w.Ui.Input.Keys)
        {
            if (key.Code == UIKeyCode.Delete)
                AssetDialogs.Open(state, AssetDialogKind.Delete, entry);

            // F2 is not one of the keys the core acts on, so it arrives with no code and the
            // platform's own name for it - which is exactly the case that split is there for.
            else if (key.Code == UIKeyCode.None && key.Name == "F2")
                AssetDialogs.Open(state, AssetDialogKind.Rename, entry);
        }
    }

    /// <summary>The asset the selection names, or null when it names something else or nothing.</summary>
    private static AssetEntry? Selected(Selection selection, AssetLibrary library) => selection switch
    {
        Selection.MaterialAsset m => library.Find(m.Guid),
        Selection.MeshAsset me => library.Find(me.Guid),
        Selection.TextureAsset t => library.Find(t.Guid),
        _ => null,
    };

    private static Selection SelectionFor(AssetEntry entry) => entry switch
    {
        MaterialAssetEntry m => new Selection.MaterialAsset(m.Guid),
        _ when entry.Kind == AssetKind.Mesh => new Selection.MeshAsset(entry.Guid),
        _ when entry.Kind == AssetKind.Texture => new Selection.TextureAsset(entry.Guid),
        _ => Selection.Empty,
    };

    private static bool IsSelected(Selection selection, AssetEntry entry) => selection switch
    {
        Selection.MaterialAsset m => m.Guid == entry.Guid,
        Selection.MeshAsset me => me.Guid == entry.Guid,
        Selection.TextureAsset t => t.Guid == entry.Guid,
        _ => false,
    };
}
