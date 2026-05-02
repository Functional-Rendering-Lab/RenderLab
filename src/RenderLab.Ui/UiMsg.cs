using System.Numerics;
using RenderLab.Assets;
using RenderLab.Scene;

namespace RenderLab.Ui;

/// <summary>
/// Messages emitted by the view in response to widget interaction. Coarse-grained:
/// one message per panel carries the whole updated subrecord, so the view composes
/// existing value-in/value-out widgets (<c>DebugFields</c>) without exploding into
/// one message per field. <see cref="UiUpdate.Apply"/> folds these into a new
/// <see cref="UiModel"/>. Light edits carry a <see cref="Light"/> (point or
/// directional) so the same message handles both variants. Drawable edits route
/// by <see cref="EditableDrawable.LocalId"/>.
/// </summary>
public abstract record UiMsg
{
    public sealed record UpdateCamera(FreeCameraState Camera) : UiMsg;
    public sealed record UpdateLight(int Index, Light Light) : UiMsg;
    public sealed record AddPointLight() : UiMsg;
    public sealed record AddDirectionalLight() : UiMsg;
    public sealed record RemoveLight(int Index) : UiMsg;
    public sealed record UpdateAmbient(HemisphericAmbient Ambient) : UiMsg;
    public sealed record AddDrawable(string Name, MeshId Mesh, Transform Transform, MaterialParams Material, TextureId AlbedoMap) : UiMsg;
    public sealed record RemoveDrawable(Guid LocalId) : UiMsg;
    public sealed record SelectDrawable(Guid? LocalId) : UiMsg;
    public sealed record SetDrawableTransform(Guid LocalId, Transform Transform) : UiMsg;
    public sealed record SetDrawableMaterial(Guid LocalId, MaterialParams Material) : UiMsg;
    public sealed record SetShading(ShadingMode Mode) : UiMsg;
    public sealed record SetLightingOnly(bool On) : UiMsg;
    public sealed record SetViz(VisualizationMode Mode) : UiMsg;
    public sealed record SetClearColor(Vector3 Color) : UiMsg;
}
