using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Per-surface Blinn-Phong parameters. <c>Albedo</c> is written straight into
/// <c>gAlbedo.rgb</c>; <c>SpecularStrength</c> (0..1) lands in <c>gNormal.a</c>;
/// <c>Shininess</c> is normalised by <see cref="ShininessRange"/> into
/// <c>gAlbedo.a</c> on write and reconstructed on read in <c>lighting.frag</c>.
/// </summary>
public sealed record MaterialParams(
    Vector3 Albedo,
    float SpecularStrength,
    float Shininess)
{
    /// <summary>Maximum shininess representable in the GBuffer alpha encoding.</summary>
    public const float ShininessRange = 256f;

    public static readonly MaterialParams Default = new(
        Albedo: new Vector3(0.6f, 0.6f, 0.6f),
        SpecularStrength: 0.5f,
        Shininess: 32f);
}
