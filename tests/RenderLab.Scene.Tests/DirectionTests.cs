using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Scene.Tests;

public class DirectionTests
{
    [Fact]
    public void Create_NormalizesNonUnitInput()
    {
        var r = Direction.Create(new Vector3(0f, -2f, 0f));
        Assert.True(r.IsOk);
        var d = r.Match(ok: x => x, error: _ => default);
        Assert.Equal(1f, d.Value.Length(), 5);
        Assert.Equal(new Vector3(0f, -1f, 0f), d.Value);
    }

    [Fact]
    public void Create_RejectsZeroVector()
    {
        var r = Direction.Create(Vector3.Zero);
        Assert.True(r.IsError);
    }
}

public class IntensityTests
{
    [Fact]
    public void Of_AcceptsNonNegative()
    {
        Assert.Equal(0f, Intensity.Of(0f).Match(ok: x => x.Value, error: _ => float.NaN));
        Assert.Equal(2.5f, Intensity.Of(2.5f).Match(ok: x => x.Value, error: _ => float.NaN));
    }

    [Fact]
    public void Of_RejectsNegative()
    {
        Assert.True(Intensity.Of(-0.01f).IsError);
    }
}
