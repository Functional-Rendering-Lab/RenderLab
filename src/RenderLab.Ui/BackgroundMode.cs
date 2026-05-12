namespace RenderLab.Ui;

/// <summary>
/// How the camera fills pixels with no geometry. <see cref="SolidColor"/> uses the
/// clear color verbatim; <see cref="AmbientGradient"/> blends the hemispheric
/// ambient ground/sky colors along the view ray's world-Y axis, giving a horizon.
/// </summary>
public enum BackgroundMode
{
    SolidColor,
    AmbientGradient,
}
