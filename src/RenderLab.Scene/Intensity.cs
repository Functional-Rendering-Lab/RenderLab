using RenderLab.Functional;

namespace RenderLab.Scene;

/// <summary>
/// A non-negative linear-light intensity scalar. Constructed via <see cref="Of"/>,
/// which rejects negative values — light cannot have negative magnitude in any
/// physically meaningful sense, and downstream shading assumes ≥ 0.
/// </summary>
public readonly record struct Intensity
{
    public float Value { get; }

    private Intensity(float v) => Value = v;

    public static Result<Intensity, ValueError> Of(float v)
    {
        if (float.IsNaN(v) || !float.IsFinite(v))
            return Result.Error<Intensity, ValueError>(new ValueError.NotFinite("Intensity"));
        if (v < 0f)
            return Result.Error<Intensity, ValueError>(new ValueError.OutOfRange("Intensity", v, ">= 0"));
        return Result.Ok<Intensity, ValueError>(new Intensity(v));
    }

    public static Intensity UnsafeFrom(float v) => new(v);

    public static implicit operator float(Intensity i) => i.Value;
}
