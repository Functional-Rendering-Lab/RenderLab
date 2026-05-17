using RenderLab.Functional;

namespace RenderLab.Scene;

/// <summary>
/// A perspective field-of-view angle in radians, strictly inside <c>(0, π)</c>.
/// </summary>
public readonly record struct Fov
{
    public float Radians { get; }

    private Fov(float v) => Radians = v;

    public static Result<Fov, ValueError> FromRadians(float radians)
    {
        if (!float.IsFinite(radians))
            return Result.Error<Fov, ValueError>(new ValueError.NotFinite("Fov"));
        if (radians <= 0f || radians >= MathF.PI)
            return Result.Error<Fov, ValueError>(new ValueError.OutOfRange("Fov", radians, "(0, π)"));
        return Result.Ok<Fov, ValueError>(new Fov(radians));
    }

    public static Result<Fov, ValueError> FromDegrees(float degrees) =>
        FromRadians(degrees * MathF.PI / 180f);

    public static Fov UnsafeFromRadians(float radians) => new(radians);

    public static implicit operator float(Fov f) => f.Radians;
}
