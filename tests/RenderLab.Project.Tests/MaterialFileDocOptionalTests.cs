using RenderLab.Functional;
using RenderLab.Project;

namespace RenderLab.Project.Tests;

/// <summary>
/// Confirms the on-disk JSON shape stays unchanged after switching
/// <c>MaterialFileDoc.AlbedoTex</c> from a nullable <see cref="AssetRef"/>
/// to <see cref="Optional{T}"/>: <c>None</c> serialises as <c>null</c>
/// (omitted under WhenWritingNull), <c>Some(v)</c> serialises as the inner
/// AssetRef.
/// </summary>
public class MaterialFileDocOptionalTests
{
    [Fact]
    public void RoundTrip_None_AlbedoTex_Persists_As_None()
    {
        var doc = new MaterialFileDoc(
            Version: 1,
            Name: "noTex",
            Params: new MaterialParamsDoc([0.5f, 0.5f, 0.5f], 0.5f, 32f),
            AlbedoTex: Optional<AssetRef>.None);

        var tmp = Path.GetTempFileName();
        try
        {
            AssetLibraryScanner.WriteMaterial(tmp, doc);
            var read = AssetLibraryScanner.TryReadMaterial(tmp)!;
            Assert.True(read.AlbedoTex.IsNone);
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void RoundTrip_Some_AlbedoTex_Preserves_Guid()
    {
        var guid = Guid.NewGuid();
        var doc = new MaterialFileDoc(
            Version: 1,
            Name: "tex",
            Params: new MaterialParamsDoc([0.5f, 0.5f, 0.5f], 0.5f, 32f),
            AlbedoTex: Optional<AssetRef>.Some(new AssetRef(guid)));

        var tmp = Path.GetTempFileName();
        try
        {
            AssetLibraryScanner.WriteMaterial(tmp, doc);
            var read = AssetLibraryScanner.TryReadMaterial(tmp)!;
            Assert.True(read.AlbedoTex.IsSome);
            var got = read.AlbedoTex.Match(some: x => x.Guid, none: () => Guid.Empty);
            Assert.Equal(guid, got);
        }
        finally { File.Delete(tmp); }
    }
}
