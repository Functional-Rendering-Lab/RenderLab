using System.Collections.Immutable;

namespace RenderLab.Ui;

/// <summary>
/// App-shell state, separate from the per-pipeline <see cref="UiModel"/>.
/// Holds the visible debug-panel set, an exit request the loop drains, and
/// project / scene metadata the menu reads (project name, active scene path,
/// the manifest's full scene list). Project / scene fields are updated by
/// the shell directly — they're notifications of completed work, not user
/// requests, so they don't flow through the reducer.
/// </summary>
public sealed record AppUiModel(
    bool RequestedExit,
    ImmutableHashSet<PanelId> VisiblePanels,
    string ProjectName,
    string ActiveScenePath,
    ImmutableArray<string> AvailableScenes,
    bool SceneDirty)
{
    private static readonly ImmutableHashSet<PanelId> AllPanels =
        ImmutableHashSet.CreateRange(Enum.GetValues<PanelId>());

    public static AppUiModel Default { get; } = new(
        RequestedExit: false,
        VisiblePanels: AllPanels,
        ProjectName: "",
        ActiveScenePath: "",
        AvailableScenes: ImmutableArray<string>.Empty,
        SceneDirty: false);

    public bool IsPanelVisible(PanelId id) => VisiblePanels.Contains(id);

    public AppUiModel WithPanelVisible(PanelId id, bool visible) => this with
    {
        VisiblePanels = visible ? VisiblePanels.Add(id) : VisiblePanels.Remove(id),
    };

    public AppUiModel WithProject(string name, string activeScene, ImmutableArray<string> scenes) => this with
    {
        ProjectName = name,
        ActiveScenePath = activeScene,
        AvailableScenes = scenes,
        SceneDirty = false,
    };

    public AppUiModel WithActiveScene(string activeScene) => this with
    {
        ActiveScenePath = activeScene,
        SceneDirty = false,
    };
}
