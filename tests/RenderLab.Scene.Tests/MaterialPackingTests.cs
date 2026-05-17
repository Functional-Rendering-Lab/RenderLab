using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Scene.Tests;

public class MaterialPackingTests
{
    [Fact]
    public void Pack_Default_LandsInValidRange()
    {
        var packed = MaterialPacking.Pack(MaterialParams.Default);

        Assert.Equal(MaterialParams.Default.Albedo.Value, packed.Albedo);
        Assert.InRange(packed.NormalAlpha, 0f, 1f);
        Assert.InRange(packed.AlbedoAlpha, 0f, 1f);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new MaterialParams(
            Albedo: Color01.UnsafeFrom(new Vector3(0.7f, 0.2f, 0.4f)),
            SpecularStrength: UnitInterval.UnsafeFrom(0.65f),
            Shininess: Shininess.UnsafeFrom(96f));

        var roundTripped = MaterialPacking.Unpack(MaterialPacking.Pack(original));

        Assert.Equal(original.Albedo.Value, roundTripped.Albedo.Value);
        Assert.Equal((float)original.SpecularStrength, (float)roundTripped.SpecularStrength, 5);
        Assert.Equal((float)original.Shininess, (float)roundTripped.Shininess, 3);
    }
}
