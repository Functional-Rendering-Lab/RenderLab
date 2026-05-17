using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Immutable point light. Position is in world space; <c>Color</c> is the
/// per-channel emission tint and <c>Intensity</c> is the linear scalar applied
/// to it before lighting accumulation. Attenuation constants currently live in
/// the lighting shader and are not modelled here. Packed to the GPU
/// <see cref="GpuLight"/> layout by <see cref="LightPacking"/>.
/// </summary>
public sealed record PointLight(
    Vector3 Position,
    Color01 Color,
    Intensity Intensity) : Light;
