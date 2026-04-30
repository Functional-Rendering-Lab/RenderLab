using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Canonical CPU-side packer that maps <see cref="PointLight"/> values into the
/// <see cref="GpuPointLight"/> layout consumed by the lighting SSBO. The runtime path
/// is shader-side (<c>lighting.frag</c> reads <c>PointLight</c> std430 entries) — this
/// codec mirrors that struct so the rule lives in one place and stays testable.
/// </summary>
public static class LightPacking
{
    public static GpuPointLight Pack(PointLight light) => new(
        PositionPad: new Vector4(light.Position, 0f),
        ColorIntensity: new Vector4(light.Color, light.Intensity));

    public static PointLight Unpack(GpuPointLight packed) => new(
        Position: new Vector3(packed.PositionPad.X, packed.PositionPad.Y, packed.PositionPad.Z),
        Color: new Vector3(packed.ColorIntensity.X, packed.ColorIntensity.Y, packed.ColorIntensity.Z),
        Intensity: packed.ColorIntensity.W);

    /// <summary>
    /// Packs <paramref name="lights"/> into <paramref name="destination"/>. Returns the
    /// number of entries written; the destination must be at least as long as the source.
    /// </summary>
    public static int PackInto(ReadOnlySpan<PointLight> lights, Span<GpuPointLight> destination)
    {
        if (destination.Length < lights.Length)
            throw new ArgumentException(
                $"Destination span ({destination.Length}) is smaller than light count ({lights.Length}).",
                nameof(destination));

        for (int i = 0; i < lights.Length; i++)
            destination[i] = Pack(lights[i]);

        return lights.Length;
    }
}
