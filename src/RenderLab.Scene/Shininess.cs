using RenderLab.Functional;

namespace RenderLab.Scene;

/// <summary>
/// Blinn-Phong shininess exponent in <c>[0, MaterialParams.ShininessRange]</c>.
/// The upper bound matches the GBuffer alpha-channel encoding.
/// </summary>
public readonly record struct Shininess
{
    public float Value { get; }

    private Shininess(float v) => Value = v;

    public static Result<Shininess, ValueError> Of(float v)
    {
        if (!float.IsFinite(v))
            return Result.Error<Shininess, ValueError>(new ValueError.NotFinite("Shininess"));
        if (v < 0f || v > MaterialParams.ShininessRange)
            return Result.Error<Shininess, ValueError>(
                new ValueError.OutOfRange("Shininess", v, $"[0, {MaterialParams.ShininessRange}]"));
        return Result.Ok<Shininess, ValueError>(new Shininess(v));
    }

    public static Shininess UnsafeFrom(float v) => new(v);

    public static implicit operator float(Shininess s) => s.Value;
}
