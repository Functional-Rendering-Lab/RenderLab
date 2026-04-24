using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Ui;

/// <summary>
/// Immutable UI / scene-editing state for a demo. One record carries everything
/// the view can edit — camera, key light, material, mesh transform, shading and
/// visualization modes. Updated by <see cref="UiUpdate.Apply"/> in response to
/// <see cref="UiMsg"/> messages emitted by the view.
/// </summary>
public sealed record UiModel(
    FreeCameraState Camera,
    PointLight KeyLight,
    MaterialParams Material,
    Transform MeshTransform,
    ShadingMode Shading,
    bool LightingOnly,
    VisualizationMode Viz,
    Vector3 ClearColor)
{
    public static UiModel Default => new(
        Camera: FreeCameraController.CreateDefault(),
        KeyLight: new PointLight(
            Position: new Vector3(2, 3, 2),
            Color: new Vector3(1f, 0.95f, 0.9f),
            Intensity: 5f),
        Material: MaterialParams.Default,
        MeshTransform: Transform.Default,
        Shading: ShadingMode.BlinnPhong,
        LightingOnly: false,
        Viz: VisualizationMode.Final,
        ClearColor: Vector3.Zero);
}
