using RenderLab.Functional;

namespace RenderLab.Scene;

/// <summary>
/// A scalar in <c>[0, 1]</c>. Used for parameters like specular strength where
/// the closed unit interval is the meaningful range.
/// </summary>
public readonly record struct UnitInterval
{
    public float Value { get; }

    private UnitInterval(float v) => Value = v;

    public static UnitInterval Zero { get; } = new(0f);
    public static UnitInterval One { get; } = new(1f);

    public static Result<UnitInterval, ValueError> Of(float v)
    {
        if (!float.IsFinite(v))
            return Result.Error<UnitInterval, ValueError>(new ValueError.NotFinite("UnitInterval"));
        if (v < 0f || v > 1f)
            return Result.Error<UnitInterval, ValueError>(new ValueError.OutOfRange("UnitInterval", v, "[0, 1]"));
        return Result.Ok<UnitInterval, ValueError>(new UnitInterval(v));
    }

    public static UnitInterval UnsafeFrom(float v) => new(v);

    public static implicit operator float(UnitInterval u) => u.Value;
}
