using Ptah.Widgets;
using RenderLab.Assets;

namespace RenderLab.Editor;

/// <summary>
/// The interface's own state, which the application owns rather than the library: which
/// drop-down is open, which colour picker, which nodes of the outliners are expanded, and what
/// the user is in the middle of typing into a dialog.
/// <para>
/// None of it belongs in <c>UiModel</c>. A model is what the scene is, and it is saved, loaded,
/// diffed for a dirty flag and folded from messages - whereas an open combo is where a gesture
/// has got to, it means nothing to the renderer, and it should not survive a reload. Ptah keeps
/// these out of its own widgets for the same reason it takes a value and returns one: the
/// library holds no state, so the application decides what the lifetime of a popup is.
/// </para>
/// <para>
/// One popup and one picker, for the whole editor, because one of each is what can be open at a
/// time. Two pickers open at once is not a feature that was cut; it is a thing no interface does.
/// Expansion is the exception and is one set for every tree in the tool, keyed by the caller's own
/// name for a node - a project path, a guid, a heading - so two trees cannot collide.
/// </para>
/// </summary>
internal sealed class WidgetState
{
    /// <summary>The open drop-down: every combo in the editor, and every context menu.</summary>
    internal readonly PopupState Popups = new();

    /// <summary>The open colour picker, and the hue and saturation it is working in.</summary>
    internal readonly ColorState Colors = new();

    /// <summary>Which nodes of the Scene, Asset Browser and Project trees are expanded.</summary>
    internal readonly TreeState Trees = new();

    /// <summary>The dialog the user is in the middle of, or null. See <see cref="AssetDialog"/>.</summary>
    internal AssetDialog? Dialog;

    /// <summary>
    /// What the Scene panel's "add from registry" picker is pointed at. It is here rather than in
    /// a pair of statics, which is where the ImGui panel kept it: a static outlives the editor and
    /// makes one panel's picker a global, and the picker is a control in a panel like any other.
    /// </summary>
    internal MeshId AddMesh = MeshId.None;

    /// <inheritdoc cref="AddMesh"/>
    internal MaterialId AddMaterial = MaterialId.None;
}
