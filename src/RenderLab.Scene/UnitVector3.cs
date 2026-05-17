using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// A non-zero unit-length 3D vector. Constructed via <see cref="Create"/>, which
/// normalizes the input and rejects the zero vector. Distinct from <see cref="Direction"/>
/// only at the type-name level today; reserved for non-directional unit-vector
/// uses (e.g. camera up).
/// </summary>
public readonly record struct UnitVector3
{
    public Vector3 Value { get; }

    private UnitVector3(Vector3 unit) => Value = unit;

    public static UnitVector3 Create(Vector3 v)
    {
        if (v.LengthSquared() < 1e-12f)
            throw new ArgumentException("UnitVector3 cannot be the zero vector.", nameof(v));
        return new UnitVector3(Vector3.Normalize(v));
    }

    public static UnitVector3 UnsafeFromUnit(Vector3 unit) => new(unit);

    public static implicit operator Vector3(UnitVector3 u) => u.Value;
}
