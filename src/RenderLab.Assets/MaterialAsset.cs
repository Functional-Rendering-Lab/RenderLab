using System.Numerics;

namespace RenderLab.Assets;

/// <summary>
/// Discriminated union root for material assets. A material asset is the
/// editable, named source of truth for shading parameters; papers translate it
/// into pass-specific push constants. New material kinds (PBR, unlit) join as
/// extra cases without touching consumers that pattern-match on the DU.
/// </summary>
public abstract record MaterialAsset(MaterialId Id, string Name);

/// <summary>
/// Blinn-Phong material: flat shading parameters plus an optional albedo map.
/// <see cref="AlbedoMap"/> = <see cref="TextureId.None"/> falls back to the
/// built-in 1×1 white texture so the shader path stays uniform.
/// </summary>
public sealed record BlinnPhongMaterial(
    MaterialId Id,
    string Name,
    Vector3 Albedo,
    float SpecularStrength,
    float Shininess,
    TextureId AlbedoMap)
    : MaterialAsset(Id, Name)
{
    public static BlinnPhongMaterial Default(MaterialId id, string name) =>
        new(id, name,
            Albedo: new Vector3(0.6f, 0.6f, 0.6f),
            SpecularStrength: 0.5f,
            Shininess: 32f,
            AlbedoMap: TextureId.None);
}
