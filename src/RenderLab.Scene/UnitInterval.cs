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

    public static UnitInterval Of(float v)
    {
        if (!float.IsFinite(v) || v < 0f || v > 1f)
            throw new ArgumentOutOfRangeException(nameof(v), v, "UnitInterval must be in [0, 1].");
        return new UnitInterval(v);
    }

    public static UnitInterval UnsafeFrom(float v) => new(v);

    public static implicit operator float(UnitInterval u) => u.Value;
}
