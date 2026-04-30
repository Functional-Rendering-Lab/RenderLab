using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Canonical CPU-side packer that maps <see cref="Light"/> values into the
/// <see cref="GpuLight"/> layout consumed by the lighting SSBO. Point lights
/// are written first, followed by directional lights — the shader iterates
/// the buffer in order and branches on the type tag, so a stable partition is
/// part of the contract. The runtime path is shader-side
/// (<c>lighting.frag</c> reads <c>Light</c> std430 entries); this codec
/// mirrors that struct so the rule lives in one place and stays testable.
/// </summary>
public static class LightPacking
{
    public static GpuLight Pack(PointLight light) => new(
        PositionType: new Vector4(light.Position, GpuLight.TypePoint),
        DirectionPad: Vector4.Zero,
        ColorIntensity: new Vector4(light.Color, light.Intensity.Value));

    public static GpuLight Pack(DirectionalLight light) => new(
        PositionType: new Vector4(0f, 0f, 0f, GpuLight.TypeDirectional),
        DirectionPad: new Vector4(light.Direction.Value, 0f),
        ColorIntensity: new Vector4(light.Color, light.Intensity.Value));

    /// <summary>
    /// Packs <paramref name="lights"/> into <paramref name="destination"/>, writing
    /// all point lights first and then all directional lights. Returns the number
    /// of entries written; the destination must be at least as long as the source.
    /// </summary>
    public static int PackInto(ReadOnlySpan<Light> lights, Span<GpuLight> destination)
    {
        if (destination.Length < lights.Length)
            throw new ArgumentException(
                $"Destination span ({destination.Length}) is smaller than light count ({lights.Length}).",
                nameof(destination));

        int written = 0;

        for (int i = 0; i < lights.Length; i++)
            if (lights[i] is PointLight p)
                destination[written++] = Pack(p);

        for (int i = 0; i < lights.Length; i++)
            if (lights[i] is DirectionalLight d)
                destination[written++] = Pack(d);

        return written;
    }
}
