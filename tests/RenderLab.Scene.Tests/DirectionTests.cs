using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Scene.Tests;

public class DirectionTests
{
    [Fact]
    public void Create_NormalizesNonUnitInput()
    {
        var d = Direction.Create(new Vector3(0f, -2f, 0f));
        Assert.Equal(1f, d.Value.Length(), 5);
        Assert.Equal(new Vector3(0f, -1f, 0f), d.Value);
    }

    [Fact]
    public void Create_RejectsZeroVector()
    {
        Assert.Throws<ArgumentException>(() => Direction.Create(Vector3.Zero));
    }
}

public class IntensityTests
{
    [Fact]
    public void Of_AcceptsNonNegative()
    {
        Assert.Equal(0f, Intensity.Of(0f).Value);
        Assert.Equal(2.5f, Intensity.Of(2.5f).Value);
    }

    [Fact]
    public void Of_RejectsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Intensity.Of(-0.01f));
    }
}
