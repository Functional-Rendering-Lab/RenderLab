namespace RenderLab.Ui;

/// <summary>
/// Identifies a dockable debug panel. The <see cref="AppUiModel"/> tracks
/// visibility per panel; <see cref="AppUiMsg.TogglePanel"/> flips one.
/// </summary>
public enum PanelId
{
    GpuTimings,
    Visualization,
    Camera,
    Lighting,
    Sphere,
    RenderGraph,
}
