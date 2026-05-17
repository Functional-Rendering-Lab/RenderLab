using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Hemispheric ambient term: a sky color blended with a ground color along the
/// surface normal's up component. Modelled as a single record because the pair
/// is semantically one value — you never want a sky tint without its matching
/// ground tint, and folding both into one type keeps the lighting push-constant
/// boundary tidy.
/// </summary>
public sealed record HemisphericAmbient(Color01 Sky, Color01 Ground)
{
    /// <summary>
    /// Soft warm-white sky over a desaturated warm-earth ground. Intentionally
    /// non-zero so a scene with no lights still reads, and so the directional-only
    /// case demos the ambient gradient cleanly.
    /// </summary>
    public static HemisphericAmbient Default { get; } = new(
        Sky: Color01.UnsafeFrom(new Vector3(0.50f, 0.55f, 0.60f)),
        Ground: Color01.UnsafeFrom(new Vector3(0.15f, 0.12f, 0.10f)));
}
