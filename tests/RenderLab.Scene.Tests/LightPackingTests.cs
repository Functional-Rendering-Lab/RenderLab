using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Scene.Tests;

public class LightPackingTests
{
    [Fact]
    public void Pack_PointLight_PlacesFieldsInExpectedSlots()
    {
        var light = new PointLight(
            Position: new Vector3(1f, 2f, 3f),
            Color: new Vector3(0.4f, 0.5f, 0.6f),
            Intensity: Intensity.Of(7f));

        var packed = LightPacking.Pack(light);

        Assert.Equal(new Vector4(1f, 2f, 3f, GpuLight.TypePoint), packed.PositionType);
        Assert.Equal(Vector4.Zero, packed.DirectionPad);
        Assert.Equal(new Vector4(0.4f, 0.5f, 0.6f, 7f), packed.ColorIntensity);
    }

    [Fact]
    public void Pack_DirectionalLight_PlacesFieldsInExpectedSlots()
    {
        var light = new DirectionalLight(
            Direction: Direction.Create(new Vector3(0f, -1f, 0f)),
            Color: new Vector3(1f, 0.95f, 0.85f),
            Intensity: Intensity.Of(2f));

        var packed = LightPacking.Pack(light);

        Assert.Equal(GpuLight.TypeDirectional, packed.PositionType.W);
        Assert.Equal(new Vector3(0f, 0f, 0f), new Vector3(packed.PositionType.X, packed.PositionType.Y, packed.PositionType.Z));
        Assert.Equal(new Vector4(0f, -1f, 0f, 0f), packed.DirectionPad);
        Assert.Equal(new Vector4(1f, 0.95f, 0.85f, 2f), packed.ColorIntensity);
    }

    [Fact]
    public void PackInto_MixedList_PartitionsPointsBeforeDirectionals()
    {
        var lights = new Light[]
        {
            new DirectionalLight(Direction.Create(new Vector3(0f, -1f, 0f)), new Vector3(1f, 1f, 1f), Intensity.Of(1f)),
            new PointLight(new Vector3(1, 0, 0), new Vector3(1, 0, 0), Intensity.Of(1f)),
            new DirectionalLight(Direction.Create(new Vector3(1f, 0f, 0f)), new Vector3(0f, 1f, 0f), Intensity.Of(0.5f)),
            new PointLight(new Vector3(0, 1, 0), new Vector3(0, 1, 0), Intensity.Of(2f)),
        };
        var dest = new GpuLight[lights.Length];

        var written = LightPacking.PackInto(lights, dest);

        Assert.Equal(4, written);
        // Two points first, in their original relative order.
        Assert.Equal(GpuLight.TypePoint, dest[0].PositionType.W);
        Assert.Equal(new Vector3(1, 0, 0), new Vector3(dest[0].PositionType.X, dest[0].PositionType.Y, dest[0].PositionType.Z));
        Assert.Equal(GpuLight.TypePoint, dest[1].PositionType.W);
        Assert.Equal(new Vector3(0, 1, 0), new Vector3(dest[1].PositionType.X, dest[1].PositionType.Y, dest[1].PositionType.Z));
        // Two directionals, in their original relative order.
        Assert.Equal(GpuLight.TypeDirectional, dest[2].PositionType.W);
        Assert.Equal(new Vector3(0f, -1f, 0f), new Vector3(dest[2].DirectionPad.X, dest[2].DirectionPad.Y, dest[2].DirectionPad.Z));
        Assert.Equal(GpuLight.TypeDirectional, dest[3].PositionType.W);
        Assert.Equal(new Vector3(1f, 0f, 0f), new Vector3(dest[3].DirectionPad.X, dest[3].DirectionPad.Y, dest[3].DirectionPad.Z));
    }

    [Fact]
    public void PackInto_LeavesTailUntouched()
    {
        var lights = new Light[]
        {
            new PointLight(new Vector3(1, 0, 0), new Vector3(1, 0, 0), Intensity.Of(1f)),
            new PointLight(new Vector3(0, 1, 0), new Vector3(0, 1, 0), Intensity.Of(2f)),
        };
        var sentinel = new GpuLight(
            new Vector4(99f, 99f, 99f, 99f),
            new Vector4(99f, 99f, 99f, 99f),
            new Vector4(99f, 99f, 99f, 99f));
        var dest = new GpuLight[4];
        Array.Fill(dest, sentinel);

        var written = LightPacking.PackInto(lights, dest);

        Assert.Equal(2, written);
        Assert.Equal(sentinel, dest[2]);
        Assert.Equal(sentinel, dest[3]);
    }

    [Fact]
    public void PackInto_ThrowsWhenDestinationTooSmall()
    {
        var lights = new Light[]
        {
            new PointLight(Vector3.Zero, Vector3.One, Intensity.Of(1f)),
            new PointLight(Vector3.One, Vector3.One, Intensity.Of(1f)),
        };
        var dest = new GpuLight[1];

        Assert.Throws<ArgumentException>(() => LightPacking.PackInto(lights, dest));
    }
}
