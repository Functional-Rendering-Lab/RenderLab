using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// World-space placement for a mesh: translation, rotation, and uniform scale.
/// Rotation is canonical as a unit <see cref="UnitQuaternion"/>; the editor
/// presents it as Euler XYZ degrees but persists the quaternion.
/// </summary>
public readonly record struct Transform(Vector3 Position, UnitQuaternion Rotation, PositiveScale Scale)
{
    public static readonly Transform Default = new(Vector3.Zero, UnitQuaternion.Identity, PositiveScale.One);

    /// <summary>Convenience for the common no-rotation case.</summary>
    public Transform(Vector3 position, PositiveScale scale) : this(position, UnitQuaternion.Identity, scale) { }

    public Matrix4x4 Matrix =>
        Matrix4x4.CreateScale(Scale.Value)
        * Matrix4x4.CreateFromQuaternion(Rotation.Value)
        * Matrix4x4.CreateTranslation(Position);
}
