using RenderLab.Functional;

namespace RenderLab.Scene;

/// <summary>
/// A strictly positive, finite uniform scale factor. Constructed via
/// <see cref="Of"/>, which rejects zero, negative, and non-finite values —
/// none of those describe a valid uniform scale.
/// </summary>
public readonly record struct PositiveScale
{
    public float Value { get; }

    private PositiveScale(float v) => Value = v;

    public static PositiveScale One { get; } = new(1f);

    public static Result<PositiveScale, ValueError> Of(float v)
    {
        if (!float.IsFinite(v))
            return Result.Error<PositiveScale, ValueError>(new ValueError.NotFinite("PositiveScale"));
        if (v <= 0f)
            return Result.Error<PositiveScale, ValueError>(new ValueError.OutOfRange("PositiveScale", v, "> 0"));
        return Result.Ok<PositiveScale, ValueError>(new PositiveScale(v));
    }

    public static PositiveScale UnsafeFrom(float v) => new(v);

    public static implicit operator float(PositiveScale s) => s.Value;
}
