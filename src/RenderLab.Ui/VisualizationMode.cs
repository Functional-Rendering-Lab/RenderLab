namespace RenderLab.Ui;

/// <summary>
/// Which buffer or image the final fullscreen pass displays. <c>Final</c> shows
/// the tonemapped lighting result; all other modes bypass tonemapping and visualize
/// a single GBuffer attachment or the raw HDR target.
/// </summary>
public enum VisualizationMode
{
    Final,
    Position,
    Normal,
    Albedo,
    Depth,
    HDR,
}
