namespace RenderLab.Ui;

/// <summary>
/// App-shell state, separate from the per-demo <see cref="UiModel"/>. Holds
/// which demo is running, which demo the user wants to switch to next (one-shot
/// signal consumed by the outer loop), panel visibility, and an exit request.
/// Preserved across demo switches so the user's panel layout follows them.
/// </summary>
public sealed record AppUiModel(
    DemoId CurrentDemo,
    DemoId? RequestedDemo,
    bool RequestedExit,
    bool ShowGpuTimings,
    bool ShowVisualization,
    bool ShowCamera,
    bool ShowLighting,
    bool ShowSphere,
    bool ShowRenderGraph)
{
    public static AppUiModel Default(DemoId demo) => new(
        CurrentDemo: demo,
        RequestedDemo: null,
        RequestedExit: false,
        ShowGpuTimings: true,
        ShowVisualization: true,
        ShowCamera: true,
        ShowLighting: true,
        ShowSphere: true,
        ShowRenderGraph: true);

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

    public bool IsPanelVisible(PanelId id) => id switch
    {
        PanelId.GpuTimings    => ShowGpuTimings,
        PanelId.Visualization => ShowVisualization,
        PanelId.Camera        => ShowCamera,
        PanelId.Lighting      => ShowLighting,
        PanelId.Sphere        => ShowSphere,
        PanelId.RenderGraph   => ShowRenderGraph,
        _                     => false,
    };
}
