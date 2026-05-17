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

    public static PositiveScale Of(float v)
    {
        if (!float.IsFinite(v) || v <= 0f)
            throw new ArgumentOutOfRangeException(nameof(v), v, "PositiveScale must be > 0 and finite.");
        return new PositiveScale(v);
    }

    public static PositiveScale UnsafeFrom(float v) => new(v);

    public static implicit operator float(PositiveScale s) => s.Value;
}
