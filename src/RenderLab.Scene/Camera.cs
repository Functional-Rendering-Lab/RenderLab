using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Immutable camera producing view and projection matrices.
/// All fields are value-type; use <c>with</c> expressions or <see cref="WithAspect"/> to derive variants.
/// </summary>
public sealed record Camera(
    Vector3 Position,
    Vector3 Target,
    Vector3 Up,
    float FovRadians,
    float AspectRatio,
    float NearPlane,
    float FarPlane)
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
                FovRadians, AspectRatio, NearPlane, FarPlane);
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
        Up: Vector3.UnitY,
        FovRadians: MathF.PI / 4f, // 45 degrees
        AspectRatio: aspect,
        NearPlane: 0.1f,
        FarPlane: 100f);
}
