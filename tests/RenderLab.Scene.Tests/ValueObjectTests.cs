using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Scene.Tests;

public class ValueObjectTests
{
    [Fact]
    public void UnitVector3_rejects_zero_vector() =>
        Assert.True(UnitVector3.Create(Vector3.Zero).IsError);

    [Fact]
    public void UnitVector3_normalizes_input()
    {
        var u = UnitVector3.Create(new Vector3(2f, 0f, 0f)).Match(ok: x => x, error: _ => default);
        Assert.Equal(Vector3.UnitX, u.Value);
    }

    [Fact]
    public void UnitQuaternion_rejects_zero_quaternion() =>
        Assert.True(UnitQuaternion.Create(new Quaternion(0, 0, 0, 0)).IsError);

    [Fact]
    public void UnitQuaternion_identity_is_unit() =>
        Assert.Equal(1f, UnitQuaternion.Identity.Value.LengthSquared(), 5);

    [Fact]
    public void PositiveScale_rejects_zero() =>
        Assert.True(PositiveScale.Of(0f).IsError);

    [Fact]
    public void PositiveScale_rejects_negative() =>
        Assert.True(PositiveScale.Of(-1f).IsError);

    [Fact]
    public void PositiveScale_rejects_non_finite() =>
        Assert.True(PositiveScale.Of(float.PositiveInfinity).IsError);

    [Fact]
    public void Color01_rejects_negative_channel() =>
        Assert.True(Color01.Of(new Vector3(-0.1f, 0f, 0f)).IsError);

    [Fact]
    public void Color01_accepts_hdr_above_one()
    {
        var c = Color01.Of(new Vector3(5f, 5f, 5f)).Match(ok: x => x, error: _ => default);
        Assert.Equal(5f, c.Value.X);
    }

    [Fact]
    public void UnitInterval_rejects_negative() =>
        Assert.True(UnitInterval.Of(-0.01f).IsError);

    [Fact]
    public void UnitInterval_rejects_above_one() =>
        Assert.True(UnitInterval.Of(1.01f).IsError);

    [Fact]
    public void Shininess_rejects_above_range() =>
        Assert.True(Shininess.Of(MaterialParams.ShininessRange + 1f).IsError);

    [Fact]
    public void Shininess_rejects_negative() =>
        Assert.True(Shininess.Of(-1f).IsError);

    [Fact]
    public void Fov_rejects_zero() =>
        Assert.True(Fov.FromRadians(0f).IsError);

    [Fact]
    public void Fov_rejects_pi() =>
        Assert.True(Fov.FromRadians(MathF.PI).IsError);

    [Fact]
    public void ClipPlanes_rejects_near_ge_far() =>
        Assert.True(ClipPlanes.Of(1f, 1f).IsError);

    [Fact]
    public void ClipPlanes_rejects_zero_near() =>
        Assert.True(ClipPlanes.Of(0f, 1f).IsError);

    [Fact]
    public void ClipPlanes_accepts_valid_pair()
    {
        var c = ClipPlanes.Of(0.1f, 100f).Match(ok: x => x, error: _ => default);
        Assert.Equal(0.1f, c.Near);
        Assert.Equal(100f, c.Far);
    }
}
