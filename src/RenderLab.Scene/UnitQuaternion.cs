using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// A unit-length quaternion. Constructed via <see cref="Create"/>, which
/// normalizes the input and rejects the zero quaternion. Used for
/// <see cref="Transform.Rotation"/> so non-unit quaternions cannot leak into
/// the scene graph and silently warp matrices.
/// </summary>
public readonly record struct UnitQuaternion
{
    public Quaternion Value { get; }

    private UnitQuaternion(Quaternion unit) => Value = unit;

    public static UnitQuaternion Identity { get; } = new(Quaternion.Identity);

    public static UnitQuaternion Create(Quaternion q)
    {
        float lenSq = q.LengthSquared();
        if (lenSq < 1e-12f || !float.IsFinite(lenSq))
            throw new ArgumentException("UnitQuaternion cannot be the zero or non-finite quaternion.", nameof(q));
        return new UnitQuaternion(Quaternion.Normalize(q));
    }

    public static UnitQuaternion UnsafeFromUnit(Quaternion unit) => new(unit);

    public static UnitQuaternion Slerp(UnitQuaternion a, UnitQuaternion b, float t) =>
        new(Quaternion.Normalize(Quaternion.Slerp(a.Value, b.Value, t)));

    public static implicit operator Quaternion(UnitQuaternion u) => u.Value;
}
