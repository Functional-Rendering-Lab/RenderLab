using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Scene.Tests;

public class LightPackingTests
{
    [Fact]
    public void Pack_PlacesFieldsInExpectedSlots()
    {
        var light = new PointLight(
            Position: new Vector3(1f, 2f, 3f),
            Color: new Vector3(0.4f, 0.5f, 0.6f),
            Intensity: 7f);

        var packed = LightPacking.Pack(light);

        Assert.Equal(new Vector4(1f, 2f, 3f, 0f), packed.PositionPad);
        Assert.Equal(new Vector4(0.4f, 0.5f, 0.6f, 7f), packed.ColorIntensity);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new PointLight(
            Position: new Vector3(-2.5f, 4f, 11f),
            Color: new Vector3(0.9f, 0.1f, 0.25f),
            Intensity: 3.2f);

        var roundTripped = LightPacking.Unpack(LightPacking.Pack(original));

        Assert.Equal(original.Position, roundTripped.Position);
        Assert.Equal(original.Color, roundTripped.Color);
        Assert.Equal(original.Intensity, roundTripped.Intensity);
    }

    [Fact]
    public void PackInto_WritesAllEntriesAndLeavesTailUntouched()
    {
        var lights = new[]
        {
            new PointLight(new Vector3(1, 0, 0), new Vector3(1, 0, 0), 1f),
            new PointLight(new Vector3(0, 1, 0), new Vector3(0, 1, 0), 2f),
        };
        var sentinel = new GpuPointLight(
            new Vector4(99f, 99f, 99f, 99f),
            new Vector4(99f, 99f, 99f, 99f));
        var dest = new GpuPointLight[4];
        Array.Fill(dest, sentinel);

        var written = LightPacking.PackInto(lights, dest);

        Assert.Equal(2, written);
        Assert.Equal(LightPacking.Pack(lights[0]), dest[0]);
        Assert.Equal(LightPacking.Pack(lights[1]), dest[1]);
        Assert.Equal(sentinel, dest[2]);
        Assert.Equal(sentinel, dest[3]);
    }

    [Fact]
    public void PackInto_ThrowsWhenDestinationTooSmall()
    {
        var lights = new[]
        {
            new PointLight(Vector3.Zero, Vector3.One, 1f),
            new PointLight(Vector3.One, Vector3.One, 1f),
        };
        var dest = new GpuPointLight[1];

        Assert.Throws<ArgumentException>(() => LightPacking.PackInto(lights, dest));
    }
}
