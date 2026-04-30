using System.Numerics;
using System.Runtime.InteropServices;

namespace RenderLab.Scene;

/// <summary>
/// GPU-side layout of a <see cref="Light"/>, std430-aligned and paired with the
/// <c>Light</c> struct in <c>lighting.frag</c>. Three <see cref="Vector4"/>s, 48 bytes:
/// <list type="bullet">
///   <item><c>PositionType.xyz</c> — world-space position (point lights only); <c>w</c> is the type tag (0 = point, 1 = directional).</item>
///   <item><c>DirectionPad.xyz</c> — unit direction pointing FROM the light into the scene (directional lights only); <c>w</c> unused.</item>
///   <item><c>ColorIntensity.rgb</c> — emission tint; <c>a</c> is the linear intensity scalar.</item>
/// </list>
/// Per-variant fields are zero-filled when not applicable so the shader can branch on the type tag without reading uninitialised memory.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct GpuLight(
    Vector4 PositionType,
    Vector4 DirectionPad,
    Vector4 ColorIntensity)
{
    public const int TypePoint = 0;
    public const int TypeDirectional = 1;
}
