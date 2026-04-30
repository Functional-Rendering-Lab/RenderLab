namespace RenderLab.Scene;

/// <summary>
/// Immutable directional light. <see cref="Direction"/> points <i>from</i> the
/// light source into the scene (i.e. a sun overhead has direction roughly
/// <c>(0, -1, 0)</c>). No positional falloff. <c>Color</c> is the emission tint
/// and <c>Intensity</c> the linear scalar. Packed to the GPU
/// <see cref="GpuLight"/> layout by <see cref="LightPacking"/>.
/// </summary>
public sealed record DirectionalLight(
    Direction Direction,
    System.Numerics.Vector3 Color,
    Intensity Intensity) : Light;
