using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// World-space placement for a mesh: translation, rotation, and uniform scale.
/// Rotation is canonical as a unit <see cref="Quaternion"/>; the editor
/// presents it as Euler XYZ degrees but persists the quaternion.
/// </summary>
public readonly record struct Transform(Vector3 Position, Quaternion Rotation, float Scale)
{
    public static readonly Transform Default = new(Vector3.Zero, Quaternion.Identity, 1f);

    /// <summary>Convenience for the common no-rotation case.</summary>
    public Transform(Vector3 position, float scale) : this(position, Quaternion.Identity, scale) { }

    public Matrix4x4 Matrix =>
        Matrix4x4.CreateScale(Scale)
        * Matrix4x4.CreateFromQuaternion(Rotation)
        * Matrix4x4.CreateTranslation(Position);
}
