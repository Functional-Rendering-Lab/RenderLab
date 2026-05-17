using System.Numerics;
using RenderLab.Functional;

namespace RenderLab.Scene;

/// <summary>
/// A linear-light RGB color with per-channel <c>≥ 0</c> and finite components.
/// HDR-friendly: no upper bound on channels. Constructed via <see cref="Of(Vector3)"/>,
/// which rejects negatives and non-finite values — negative emission is
/// physically meaningless and downstream shading assumes non-negative input.
/// </summary>
public readonly record struct Color01
{
    public Vector3 Value { get; }

    private Color01(Vector3 v) => Value = v;

    public static Color01 Black { get; } = new(Vector3.Zero);

    public static Result<Color01, ValueError> Of(Vector3 v)
    {
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
            return Result.Error<Color01, ValueError>(new ValueError.NotFinite("Color01"));
        if (v.X < 0f || v.Y < 0f || v.Z < 0f)
            return Result.Error<Color01, ValueError>(new ValueError.OutOfRange("Color01", Min(v), ">= 0"));
        return Result.Ok<Color01, ValueError>(new Color01(v));
    }

    public static Result<Color01, ValueError> Of(float r, float g, float b) => Of(new Vector3(r, g, b));

    public static Color01 UnsafeFrom(Vector3 v) => new(v);

    public static implicit operator Vector3(Color01 c) => c.Value;

    private static float Min(Vector3 v) => MathF.Min(v.X, MathF.Min(v.Y, v.Z));
}
