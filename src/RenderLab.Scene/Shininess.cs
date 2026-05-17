namespace RenderLab.Scene;

/// <summary>
/// Blinn-Phong shininess exponent in <c>[0, MaterialParams.ShininessRange]</c>.
/// The upper bound matches the GBuffer alpha-channel encoding.
/// </summary>
public readonly record struct Shininess
{
    public float Value { get; }

    private Shininess(float v) => Value = v;

    public static Shininess Of(float v)
    {
        if (!float.IsFinite(v) || v < 0f || v > MaterialParams.ShininessRange)
            throw new ArgumentOutOfRangeException(nameof(v), v,
                $"Shininess must be in [0, {MaterialParams.ShininessRange}].");
        return new Shininess(v);
    }

    public static Shininess UnsafeFrom(float v) => new(v);

    public static implicit operator float(Shininess s) => s.Value;
}
