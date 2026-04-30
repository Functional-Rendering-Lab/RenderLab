using System.Numerics;
using System.Runtime.InteropServices;

namespace RenderLab.Scene;

/// <summary>
/// GPU-side layout of a <see cref="PointLight"/>, std430-aligned and paired with the
/// <c>PointLight</c> struct in <c>lighting.frag</c>. Two <see cref="Vector4"/>s, 32 bytes:
/// <c>PositionPad.xyz</c> is world-space position (w unused), <c>ColorIntensity.rgb</c> is
/// the emission tint and <c>ColorIntensity.a</c> is the linear intensity scalar.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct GpuPointLight(
    Vector4 PositionPad,
    Vector4 ColorIntensity);
