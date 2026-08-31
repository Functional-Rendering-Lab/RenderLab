using Ptah;
using Ptah.Widgets;
using RenderLab.Project;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>Which of the two questions the editor stops to ask.</summary>
internal enum AssetDialogKind
{
    Rename,
    Delete,
}

/// <summary>
/// The dialog the user is in the middle of: what is being asked, which asset it is being asked
/// about, and - for a rename - the name being typed.
/// <para>
/// A class rather than a record, because the draft is written through a <c>ref</c> by the text
/// field and read back on the frame the user commits it. The asset is held by guid rather than by
/// entry: the library is rescanned whenever anything on disk changes, so an entry captured when
/// the dialog opened would be a stale copy of a record that may no longer exist.
/// </para>
/// </summary>
internal sealed class AssetDialog
{
    internal AssetDialogKind Kind;
    internal Guid Guid;
    internal string Draft = string.Empty;

    /// <summary>Whether the caret has been put in the field yet. See <c>AssetDialogs.Rename</c>.</summary>
    internal bool Focused;
}

/// <summary>
/// The two dialogs the Asset Browser opens, drawn once per frame from
/// <see cref="WidgetState.Dialog"/> rather than once per row.
/// <para>
/// That is the difference worth writing down about this port. Dear ImGui's popups are opened by
/// id at the site of the row they belong to, so both dialogs were built inside the row loop, once
/// for every asset in the project, and the panel needed a dictionary of drafts keyed by guid to
/// keep the one open field's contents from being clobbered by its neighbours. A dialog in Ptah is
/// chrome opened by an <c>if</c>: whether one is up is application state, there is one of it, and
/// the draft is a field on it.
/// </para>
/// <para>
/// It is also built beside the shell rather than inside the Asset Browser's body, because a modal
/// is modal to the window and not to a panel: it dims the whole screen and takes the mouse and
/// the keyboard from everything behind it, the panel that opened it included.
/// </para>
/// </summary>
internal static class AssetDialogs
{
    private const float Width = 420f;

    /// <summary>
    /// How tall each dialog is. A modal is given its size rather than sized to what it holds, so
    /// these are the sum of what goes in them - a title bar, a two-line note, a field for the
    /// rename, a row of answers - plus a margin the same size as the padding around them. Erring
    /// generous rather than tight: the body does not clip, so a dialog that guessed short would
    /// spill its buttons out of its own frame rather than scroll them.
    /// </summary>
    private const float RenameHeight = 165f;

    /// <inheritdoc cref="RenameHeight"/>
    private const float DeleteHeight = 135f;

    internal static void Draw(WidgetKit w, WidgetState state, AssetLibrary library,
        Action<AppUiMsg> dispatchApp)
    {
        if (state.Dialog is not AssetDialog dialog)
            return;

        AssetEntry? entry = library.Find(dialog.Guid);
        if (entry is null)
        {
            // The asset went while the question about it was still on screen - a rescan, an
            // import, or the delete this dialog just asked for having happened. There is nothing
            // left to rename or to confirm, so the dialog goes with it.
            state.Dialog = null;
            return;
        }

        switch (dialog.Kind)
        {
            case AssetDialogKind.Rename:
                Rename(w, state, dialog, entry, dispatchApp);
                break;
            case AssetDialogKind.Delete:
                Delete(w, state, entry, dispatchApp);
                break;
        }
    }

    /// <summary>Opens one, replacing whatever was open.</summary>
    internal static void Open(WidgetState state, AssetDialogKind kind, AssetEntry entry) =>
        state.Dialog = new AssetDialog
        {
            Kind = kind,
            Guid = entry.Guid,
            Draft = entry.Name,
        };

    private static void Rename(WidgetKit w, WidgetState state, AssetDialog dialog,
        AssetEntry entry, Action<AppUiMsg> dispatchApp)
    {
        bool apply = false;
        bool cancel;
        bool dismissed;

        using (ModalFrame modal = w.Modal("rename", $"Rename {entry.Name}", Width, RenameHeight))
        {
            Note(w, "rename_note",
                "The extension and the GUID are kept, so nothing that references this asset breaks.");

            UITextComm field = w.TextField("rename_field", ref dialog.Draft);

            // The caret goes in the field on the frame the dialog appears. A dialog whose whole
            // purpose is to take a string, opening with the keyboard pointed at nothing, costs
            // the user a click before they can start doing the only thing it is for.
            if (!dialog.Focused)
            {
                w.Ui.Focus(field.Box);
                dialog.Focused = true;
            }

            string name = dialog.Draft.Trim();
            bool canApply = name.Length > 0 &&
                            !string.Equals(name, entry.Name, StringComparison.Ordinal);

            using (w.ButtonRow("rename_buttons"))
            {
                w.Ui.Spacer(UISize.Percent(1f));
                apply = w.EnabledIf(canApply).ToolButton("Rename").Clicked;
                cancel = w.ToolButton("Cancel").Clicked;
            }

            // Enter commits, and only when the name would actually apply - a field that is empty
            // or unchanged is one the user is still in the middle of.
            apply |= field.Submitted && canApply;
            dismissed = modal.DismissRequested;
        }

        if (apply)
            dispatchApp(new AppUiMsg.RequestRenameAsset(entry.Guid, dialog.Draft.Trim()));

        if (apply || cancel || dismissed)
            state.Dialog = null;
    }

    private static void Delete(WidgetKit w, WidgetState state, AssetEntry entry,
        Action<AppUiMsg> dispatchApp)
    {
        bool confirm;
        bool cancel;
        bool dismissed;

        using (ModalFrame modal = w.Modal("delete", $"Delete {entry.Name}?", Width, DeleteHeight))
        {
            Note(w, "delete_note", "The file and its .meta sidecar are removed from disk.");

            using (w.ButtonRow("delete_buttons"))
            {
                w.Ui.Spacer(UISize.Percent(1f));
                confirm = w.ToolButton("Delete").Clicked;
                cancel = w.ToolButton("Cancel").Clicked;
            }

            dismissed = modal.DismissRequested;
        }

        if (confirm)
            dispatchApp(new AppUiMsg.RequestDeleteAsset(entry.Guid));

        if (confirm || cancel || dismissed)
            state.Dialog = null;
    }

    /// <summary>
    /// What the dialog is about to do. Wrapped to the dialog's own width rather than to the
    /// parent's, because a paragraph with no width given reads last frame's solved rectangle and
    /// a dialog's first frame is the frame it appears on - which is the one frame it would be a
    /// single line running off the edge.
    /// </summary>
    private static void Note(WidgetKit w, string key, string text)
    {
        using (w.Ui.TextColor(w.Theme.Muted))
            w.TextWrapped(key, text, Width - (w.Theme.Pad * 2f));

        w.Ui.Spacer(UISize.Pixels(w.Theme.Gap));
    }
}
