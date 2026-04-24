using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Ui;

/// <summary>
/// Messages emitted by the view in response to widget interaction. Coarse-grained:
/// one message per panel carries the whole updated subrecord, so the view composes
/// existing value-in/value-out widgets (<c>DebugFields</c>) without exploding into
/// one message per field. <see cref="UiUpdate.Apply"/> folds these into a new
/// <see cref="UiModel"/>.
/// </summary>
public abstract record UiMsg
{
    public sealed record UpdateCamera(FreeCameraState Camera) : UiMsg;
    public sealed record UpdateLight(PointLight Light) : UiMsg;
    public sealed record UpdateMaterial(MaterialParams Material) : UiMsg;
    public sealed record UpdateMeshTransform(Transform Transform) : UiMsg;
    public sealed record SetShading(ShadingMode Mode) : UiMsg;
    public sealed record SetLightingOnly(bool On) : UiMsg;
    public sealed record SetViz(VisualizationMode Mode) : UiMsg;
    public sealed record SetClearColor(Vector3 Color) : UiMsg;
}
