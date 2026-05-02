using RenderLab.Assets;
using RenderLab.Scene;

namespace RenderLab.Ui;

/// <summary>
/// Editor-side mirror of a scene <see cref="Drawable"/>: the same data plus a
/// stable <see cref="LocalId"/> the UI uses for selection and list-edit
/// operations. <see cref="Material"/> is a <see cref="MaterialId"/> pointing
/// at an asset owned by the registry; the editor mutates the asset in place
/// rather than carrying a copy of the parameters.
/// </summary>
public sealed record EditableDrawable(
    Guid LocalId,
    string Name,
    MeshId Mesh,
    Transform Transform,
    MaterialId Material);
