using System.Collections.Immutable;
using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Ui;

/// <summary>
/// Immutable UI / scene-editing state for a demo. One record carries everything
/// the view can edit — camera, lights, material, mesh transform, shading and
/// visualization modes. Updated by <see cref="UiUpdate.Apply"/> in response to
/// <see cref="UiMsg"/> messages emitted by the view.
/// </summary>
public sealed record UiModel(
    FreeCameraState Camera,
    ImmutableArray<PointLight> Lights,
    MaterialParams Material,
    Transform MeshTransform,
    ShadingMode Shading,
    bool LightingOnly,
    VisualizationMode Viz,
    Vector3 ClearColor)
{
    public static UiModel Default => new(
        Camera: FreeCameraController.CreateDefault(),
        Lights: ImmutableArray.Create(
            new PointLight(
                Position: new Vector3(2, 3, 2),
                Color: new Vector3(1f, 0.95f, 0.9f),
                Intensity: 5f),
            new PointLight(
                Position: new Vector3(-2.5f, 0.5f, 1.5f),
                Color: new Vector3(0.2f, 0.5f, 1.0f),
                Intensity: 1.5f),
            new PointLight(
                Position: new Vector3(2.5f, 0.5f, 1.5f),
                Color: new Vector3(1.0f, 0.4f, 0.2f),
                Intensity: 1.5f)),
        Material: MaterialParams.Default,
        MeshTransform: Transform.Default,
        Shading: ShadingMode.BlinnPhong,
        LightingOnly: false,
        Viz: VisualizationMode.Final,
        ClearColor: Vector3.Zero);

    /// <summary>Default seed for a new light added through the lighting panel.</summary>
    public static PointLight DefaultNewLight => new(
        Position: new Vector3(0f, 2f, 2f),
        Color: new Vector3(1f, 1f, 1f),
        Intensity: 1f);
}
