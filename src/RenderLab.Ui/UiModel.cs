using System.Collections.Immutable;
using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Ui;

/// <summary>
/// Immutable UI / scene-editing state for a demo. One record carries everything
/// the view can edit — camera, lights, hemispheric ambient, an editable list of
/// drawables (with selection), shading and visualization modes. Updated by
/// <see cref="UiUpdate.Apply"/> in response to <see cref="UiMsg"/> messages
/// emitted by the view.
/// </summary>
public sealed record UiModel(
    FreeCameraState Camera,
    ImmutableArray<Light> Lights,
    HemisphericAmbient Ambient,
    ImmutableArray<EditableDrawable> Drawables,
    Selection Selection,
    ShadingMode Shading,
    bool LightingOnly,
    VisualizationMode Viz,
    Vector3 ClearColor,
    BackgroundMode Background)
{
    public static UiModel Default => new(
        Camera: FreeCameraController.CreateDefault(),
        Lights: ImmutableArray.Create<Light>(
            new PointLight(
                Position: new Vector3(2, 3, 2),
                Color: new Vector3(1f, 0.95f, 0.9f),
                Intensity: Intensity.Of(5f)),
            new PointLight(
                Position: new Vector3(-2.5f, 0.5f, 1.5f),
                Color: new Vector3(0.2f, 0.5f, 1.0f),
                Intensity: Intensity.Of(1.5f)),
            new PointLight(
                Position: new Vector3(2.5f, 0.5f, 1.5f),
                Color: new Vector3(1.0f, 0.4f, 0.2f),
                Intensity: Intensity.Of(1.5f)),
            new DirectionalLight(
                Direction: Direction.UnsafeFromUnit(Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f))),
                Color: new Vector3(1.0f, 0.95f, 0.85f),
                Intensity: Intensity.Of(1.0f))),
        Ambient: HemisphericAmbient.Default,
        Drawables: ImmutableArray<EditableDrawable>.Empty,
        Selection: Selection.Empty,
        Shading: ShadingMode.BlinnPhong,
        LightingOnly: false,
        Viz: VisualizationMode.Final,
        ClearColor: Vector3.Zero,
        Background: BackgroundMode.SolidColor);

    /// <summary>Default seed for a new point light added through the lighting panel.</summary>
    public static PointLight DefaultNewPointLight => new(
        Position: new Vector3(0f, 2f, 2f),
        Color: new Vector3(1f, 1f, 1f),
        Intensity: Intensity.Of(1f));

    /// <summary>Default seed for a new directional light added through the lighting panel.</summary>
    public static DirectionalLight DefaultNewDirectionalLight => new(
        Direction: Direction.UnsafeFromUnit(Vector3.Normalize(new Vector3(0f, -1f, -0.2f))),
        Color: new Vector3(1f, 1f, 1f),
        Intensity: Intensity.Of(1f));
}
