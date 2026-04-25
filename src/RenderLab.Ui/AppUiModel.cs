using System.Collections.Immutable;

namespace RenderLab.Ui;

/// <summary>
/// App-shell state, separate from the per-demo <see cref="UiModel"/>. Holds
/// which demo is running, which demo the user wants to switch to next (one-shot
/// signal consumed by the outer loop), the set of visible debug panels, and an
/// exit request. Panel visibility is preserved across demo switches so the
/// user's layout follows them.
/// </summary>
public sealed record AppUiModel(
    DemoId CurrentDemo,
    DemoId? RequestedDemo,
    bool RequestedExit,
    ImmutableHashSet<PanelId> VisiblePanels)
{
    public static AppUiModel Default(DemoId demo) => new(
        CurrentDemo: demo,
        RequestedDemo: null,
        RequestedExit: false,
        VisiblePanels: AllPanels);

    private static readonly ImmutableHashSet<PanelId> AllPanels =
        ImmutableHashSet.CreateRange(Enum.GetValues<PanelId>());

    /// <summary>
    /// Copy this model into the next demo's starting state: clears the one-shot
    /// switch request and updates <see cref="CurrentDemo"/>. Panel visibility is
    /// preserved so the user's layout follows them across demos.
    /// </summary>
    public AppUiModel HandOffTo(DemoId next) => this with
    {
        CurrentDemo = next,
        RequestedDemo = null,
    };

    public bool IsPanelVisible(PanelId id) => VisiblePanels.Contains(id);

    public AppUiModel WithPanelVisible(PanelId id, bool visible) => this with
    {
        VisiblePanels = visible ? VisiblePanels.Add(id) : VisiblePanels.Remove(id),
    };
}
