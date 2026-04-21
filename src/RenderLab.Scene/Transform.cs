using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// World-space placement for a mesh: translation + uniform scale. Rotation is
/// omitted until a demo actually needs it.
/// </summary>
public readonly record struct Transform(Vector3 Position, float Scale)
{
    public static readonly Transform Default = new(Vector3.Zero, 1f);

    public Matrix4x4 Matrix =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateTranslation(Position);
}
