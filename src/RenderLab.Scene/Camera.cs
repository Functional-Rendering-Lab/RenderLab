using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Immutable camera producing view and projection matrices.
/// All fields are value-type; use <c>with</c> expressions or <see cref="WithAspect"/> to derive variants.
/// </summary>
public sealed record Camera(
    Vector3 Position,
    Vector3 Target,
    UnitVector3 Up,
    Fov Fov,
    float AspectRatio,
    ClipPlanes Clip)
{
    public Matrix4x4 ViewMatrix =>
        Matrix4x4.CreateLookAt(Position, Target, Up);

    /// <summary>
    /// Perspective projection with Y-axis flipped for Vulkan clip space
    /// (Vulkan Y points downward, unlike OpenGL).
    /// </summary>
    public Matrix4x4 ProjectionMatrix
    {
        get
        {
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(
                Fov, AspectRatio, Clip.Near, Clip.Far);
            // Vulkan clip space: Y is inverted compared to OpenGL
            proj.M22 *= -1;
            return proj;
        }
    }

    public Matrix4x4 ViewProjectionMatrix =>
        ViewMatrix * ProjectionMatrix;

    public Camera WithAspect(float aspect) =>
        this with { AspectRatio = aspect };

    public static Camera CreateDefault(float aspect) => new(
        Position: new Vector3(0, 1.5f, 3.0f),
        Target: Vector3.Zero,
        Up: UnitVector3.UnsafeFromUnit(Vector3.UnitY),
        Fov: Fov.UnsafeFromRadians(MathF.PI / 4f), // 45 degrees
        AspectRatio: aspect,
        Clip: ClipPlanes.UnsafeFrom(0.1f, 100f));
}
