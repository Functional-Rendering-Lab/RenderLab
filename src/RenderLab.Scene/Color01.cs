using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// A linear-light RGB color with per-channel <c>≥ 0</c> and finite components.
/// HDR-friendly: no upper bound on channels. Constructed via <see cref="Of"/>,
/// which rejects negatives and non-finite values — negative emission is
/// physically meaningless and downstream shading assumes non-negative input.
/// </summary>
public readonly record struct Color01
{
    public Vector3 Value { get; }

    private Color01(Vector3 v) => Value = v;

    public static Color01 Black { get; } = new(Vector3.Zero);

    public static Color01 Of(Vector3 v)
    {
        if (!IsValid(v))
            throw new ArgumentOutOfRangeException(nameof(v), v, "Color01 channels must be ≥ 0 and finite.");
        return new Color01(v);
    }

    public static Color01 Of(float r, float g, float b) => Of(new Vector3(r, g, b));

    public static Color01 UnsafeFrom(Vector3 v) => new(v);

    public static implicit operator Vector3(Color01 c) => c.Value;

    private static bool IsValid(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) &&
        v.X >= 0f && v.Y >= 0f && v.Z >= 0f;
}
