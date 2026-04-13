namespace RenderLab.Scene;

/// <summary>
/// Per-surface Blinn-Phong parameters. Packed into the GBuffer alpha channels:
/// <c>SpecularStrength</c> (0..1) lands in <c>gNormal.a</c>, and <c>Shininess</c>
/// is normalised by <see cref="ShininessRange"/> into <c>gAlbedo.a</c> on write
/// and reconstructed on read in <c>lighting.frag</c>.
/// </summary>
public sealed record MaterialParams(
    float SpecularStrength,
    float Shininess)
{
    /// <summary>Maximum shininess representable in the GBuffer alpha encoding.</summary>
    public const float ShininessRange = 256f;

    public static readonly MaterialParams Default = new(SpecularStrength: 0.5f, Shininess: 32f);
}
