using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Immutable point light. Position is in world space; <c>Color</c> is the
/// per-channel emission tint and <c>Intensity</c> is the linear scalar applied
/// to it before lighting accumulation. Attenuation constants currently live in
/// the lighting shader and are not modelled here.
/// </summary>
public sealed record PointLight(
    Vector3 Position,
    Vector3 Color,
    float Intensity);
