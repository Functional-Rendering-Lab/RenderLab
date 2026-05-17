using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Scene.Tests;

public class ValueObjectTests
{
    [Fact]
    public void UnitVector3_rejects_zero_vector() =>
        Assert.Throws<ArgumentException>(() => UnitVector3.Create(Vector3.Zero));

    [Fact]
    public void UnitVector3_normalizes_input()
    {
        var u = UnitVector3.Create(new Vector3(2f, 0f, 0f));
        Assert.Equal(Vector3.UnitX, u.Value);
    }

    [Fact]
    public void UnitQuaternion_rejects_zero_quaternion() =>
        Assert.Throws<ArgumentException>(() => UnitQuaternion.Create(new Quaternion(0, 0, 0, 0)));

    [Fact]
    public void UnitQuaternion_identity_is_unit() =>
        Assert.Equal(1f, UnitQuaternion.Identity.Value.LengthSquared(), 5);

    [Fact]
    public void PositiveScale_rejects_zero() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PositiveScale.Of(0f));

    [Fact]
    public void PositiveScale_rejects_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PositiveScale.Of(-1f));

    [Fact]
    public void PositiveScale_rejects_non_finite() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PositiveScale.Of(float.PositiveInfinity));

    [Fact]
    public void Color01_rejects_negative_channel() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Color01.Of(new Vector3(-0.1f, 0f, 0f)));

    [Fact]
    public void Color01_accepts_hdr_above_one()
    {
        var c = Color01.Of(new Vector3(5f, 5f, 5f));
        Assert.Equal(5f, c.Value.X);
    }

    [Fact]
    public void UnitInterval_rejects_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitInterval.Of(-0.01f));

    [Fact]
    public void UnitInterval_rejects_above_one() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => UnitInterval.Of(1.01f));

    [Fact]
    public void Shininess_rejects_above_range() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Shininess.Of(MaterialParams.ShininessRange + 1f));

    [Fact]
    public void Shininess_rejects_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Shininess.Of(-1f));

    [Fact]
    public void Fov_rejects_zero() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Fov.FromRadians(0f));

    [Fact]
    public void Fov_rejects_pi() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Fov.FromRadians(MathF.PI));

    [Fact]
    public void ClipPlanes_rejects_near_ge_far() =>
        Assert.Throws<ArgumentException>(() => ClipPlanes.Of(1f, 1f));

    [Fact]
    public void ClipPlanes_rejects_zero_near() =>
        Assert.Throws<ArgumentException>(() => ClipPlanes.Of(0f, 1f));

    [Fact]
    public void ClipPlanes_accepts_valid_pair()
    {
        var c = ClipPlanes.Of(0.1f, 100f);
        Assert.Equal(0.1f, c.Near);
        Assert.Equal(100f, c.Far);
    }
}
