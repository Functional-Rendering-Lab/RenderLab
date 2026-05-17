using System.Numerics;
using RenderLab.Functional;

namespace RenderLab.Scene;

/// <summary>
/// A unit-length 3D direction. Constructed via <see cref="Create"/>, which
/// normalizes the input and rejects the zero vector — so a <see cref="Direction"/>
/// value is, by construction, always a valid direction. Used for directional
/// lights, where a non-unit or zero direction would silently break shading.
/// </summary>
public readonly record struct Direction
{
    public Vector3 Value { get; }

    private Direction(Vector3 unit) => Value = unit;

    public static Result<Direction, ValueError> Create(Vector3 v)
    {
        if (v.LengthSquared() < 1e-12f)
            return Result.Error<Direction, ValueError>(new ValueError.ZeroVector("Direction"));
        return Result.Ok<Direction, ValueError>(new Direction(Vector3.Normalize(v)));
    }

    /// <summary>
    /// Wraps an already-normalized vector without re-normalizing. Use only when
    /// the caller can guarantee unit length (e.g. constants from a default seed).
    /// </summary>
    public static Direction UnsafeFromUnit(Vector3 unit) => new(unit);
}
