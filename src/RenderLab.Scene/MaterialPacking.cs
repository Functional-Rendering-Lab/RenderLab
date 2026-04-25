using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// A <see cref="MaterialParams"/> projected onto the GBuffer's storage layout:
/// <c>Albedo</c> goes to <c>gAlbedo.rgb</c>, <c>NormalAlpha</c> to <c>gNormal.a</c>,
/// <c>AlbedoAlpha</c> to <c>gAlbedo.a</c>. The XYZ channels of <c>gNormal</c> are
/// per-fragment (the shaded normal) and not part of this codec.
/// </summary>
public readonly record struct PackedMaterial(
    Vector3 Albedo,
    float NormalAlpha,
    float AlbedoAlpha);

/// <summary>
/// Canonical CPU-side definition of how <see cref="MaterialParams"/> packs into the
/// GBuffer alpha channels. The runtime path is shader-side (<c>gbuffer.frag</c> writes,
/// <c>lighting.frag</c> reads) — this codec mirrors that math so the rule is testable
/// and discoverable from one place.
/// </summary>
public static class MaterialPacking
{
    public static PackedMaterial Pack(MaterialParams material) => new(
        Albedo: material.Albedo,
        NormalAlpha: material.SpecularStrength,
        AlbedoAlpha: material.Shininess / MaterialParams.ShininessRange);

    public static MaterialParams Unpack(PackedMaterial packed) => new(
        Albedo: packed.Albedo,
        SpecularStrength: packed.NormalAlpha,
        Shininess: packed.AlbedoAlpha * MaterialParams.ShininessRange);
}
