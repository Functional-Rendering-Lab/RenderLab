namespace RenderLab.Scene;

/// <summary>
/// A non-negative linear-light intensity scalar. Constructed via <see cref="Of"/>,
/// which rejects negative values — light cannot have negative magnitude in any
/// physically meaningful sense, and downstream shading assumes ≥ 0.
/// </summary>
public readonly record struct Intensity
{
    public float Value { get; }

    private Intensity(float v) => Value = v;

    public static Intensity Of(float v)
    {
        if (v < 0f || float.IsNaN(v))
            throw new ArgumentOutOfRangeException(nameof(v), v, "Intensity must be ≥ 0.");
        return new Intensity(v);
    }

    public static implicit operator float(Intensity i) => i.Value;
}
