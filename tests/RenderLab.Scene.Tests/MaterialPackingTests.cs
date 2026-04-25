using System.Numerics;
using RenderLab.Scene;

namespace RenderLab.Scene.Tests;

public class MaterialPackingTests
{
    [Fact]
    public void Pack_Default_LandsInValidRange()
    {
        var packed = MaterialPacking.Pack(MaterialParams.Default);

        Assert.Equal(MaterialParams.Default.Albedo, packed.Albedo);
        Assert.InRange(packed.NormalAlpha, 0f, 1f);
        Assert.InRange(packed.AlbedoAlpha, 0f, 1f);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new MaterialParams(
            Albedo: new Vector3(0.7f, 0.2f, 0.4f),
            SpecularStrength: 0.65f,
            Shininess: 96f);

        var roundTripped = MaterialPacking.Unpack(MaterialPacking.Pack(original));

        Assert.Equal(original.Albedo, roundTripped.Albedo);
        Assert.Equal(original.SpecularStrength, roundTripped.SpecularStrength, 5);
        Assert.Equal(original.Shininess, roundTripped.Shininess, 3);
    }
}
