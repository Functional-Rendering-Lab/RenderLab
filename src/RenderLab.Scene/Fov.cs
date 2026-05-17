namespace RenderLab.Scene;

/// <summary>
/// A perspective field-of-view angle in radians, strictly inside <c>(0, π)</c>.
/// </summary>
public readonly record struct Fov
{
    public float Radians { get; }

    private Fov(float v) => Radians = v;

    public static Fov FromRadians(float radians)
    {
        if (!float.IsFinite(radians) || radians <= 0f || radians >= MathF.PI)
            throw new ArgumentOutOfRangeException(nameof(radians), radians, "Fov must be in (0, π) radians.");
        return new Fov(radians);
    }

    public static Fov FromDegrees(float degrees) => FromRadians(degrees * MathF.PI / 180f);

    public static Fov UnsafeFromRadians(float radians) => new(radians);

    public static implicit operator float(Fov f) => f.Radians;
}
