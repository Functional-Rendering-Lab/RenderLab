using RenderLab.Assets;
using RenderLab.Scene;

namespace RenderLab.Ui;

/// <summary>
/// Editor-side mirror of a scene <see cref="Drawable"/>: the same data plus a
/// stable <see cref="LocalId"/> the UI uses for selection and list-edit
/// operations. Material stays as <see cref="MaterialParams"/> for now and
/// becomes a <c>MaterialId</c> in Step F of the asset migration.
/// </summary>
public sealed record EditableDrawable(
    Guid LocalId,
    string Name,
    MeshId Mesh,
    Transform Transform,
    MaterialParams Material,
    TextureId AlbedoMap);
