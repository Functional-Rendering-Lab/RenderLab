using RenderLab.Project;

namespace RenderLab.Project.Tests;

public class AssetMetaTests
{
    private static string MakeTemp() => Directory.CreateTempSubdirectory("renderlab-test-").FullName;

    [Fact]
    public void ReadOrCreate_creates_sidecar_with_fresh_guid_when_missing()
    {
        var temp = MakeTemp();
        try
        {
            var asset = Path.Combine(temp, "stone.png");
            File.WriteAllText(asset, "");

            var meta = AssetMetaIO.ReadOrCreate(asset, AssetKind.Texture);

            Assert.NotEqual(System.Guid.Empty, meta.Guid);
            Assert.Equal(AssetKind.Texture, meta.Kind);
            Assert.True(File.Exists(asset + ".meta"));
            Assert.IsType<TextureImportSettings>(meta.Import);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ReadOrCreate_returns_same_guid_on_repeat_calls()
    {
        var temp = MakeTemp();
        try
        {
            var asset = Path.Combine(temp, "bunny.glb");
            File.WriteAllText(asset, "");

            var first = AssetMetaIO.ReadOrCreate(asset, AssetKind.Mesh);
            var second = AssetMetaIO.ReadOrCreate(asset, AssetKind.Mesh);

            Assert.Equal(first.Guid, second.Guid);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void TryRead_returns_null_for_missing_sidecar()
    {
        Assert.Null(AssetMetaIO.TryRead(Path.Combine(Path.GetTempPath(), "does-not-exist.meta")));
    }

    [Fact]
    public void Write_then_TryRead_round_trips_polymorphic_import_settings()
    {
        var temp = MakeTemp();
        try
        {
            var path = Path.Combine(temp, "x.png" + AssetMetaIO.MetaExtension);
            var guid = System.Guid.NewGuid();
            var src = new AssetMetaDoc(guid, AssetKind.Texture, new TextureImportSettings(SRgb: false, Mips: false));
            AssetMetaIO.Write(path, src);
            var loaded = AssetMetaIO.TryRead(path);

            Assert.NotNull(loaded);
            Assert.Equal(guid, loaded!.Guid);
            Assert.Equal(AssetKind.Texture, loaded.Kind);
            var imp = Assert.IsType<TextureImportSettings>(loaded.Import);
            Assert.False(imp.SRgb);
            Assert.False(imp.Mips);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void Scanner_creates_meta_for_importable_files_and_hides_meta_from_listing()
    {
        var temp = MakeTemp();
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "assets"));
            File.WriteAllText(Path.Combine(temp, "assets", "bunny.glb"), "");
            File.WriteAllText(Path.Combine(temp, "assets", "stone.png"), "");
            File.WriteAllText(Path.Combine(temp, "assets", "readme.md"), "");

            var idx = ProjectAssetScanner.Scan(temp);

            var assets = idx.Root.Subfolders.Single();
            // .meta sidecars exist on disk for importable kinds
            Assert.True(File.Exists(Path.Combine(temp, "assets", "bunny.glb.meta")));
            Assert.True(File.Exists(Path.Combine(temp, "assets", "stone.png.meta")));
            Assert.False(File.Exists(Path.Combine(temp, "assets", "readme.md.meta")));

            // But they are not surfaced as standalone entries
            var names = assets.Files.Select(f => f.Name).ToArray();
            Assert.Equal(new[] { "bunny.glb", "readme.md", "stone.png" }, names);

            // Importable entries carry MetaGuid; non-importable ones do not.
            var bunny = assets.Files.Single(f => f.Name == "bunny.glb");
            Assert.NotNull(bunny.MetaGuid);
            var readme = assets.Files.Single(f => f.Name == "readme.md");
            Assert.Null(readme.MetaGuid);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void Scanner_preserves_meta_guid_across_rescans()
    {
        var temp = MakeTemp();
        try
        {
            File.WriteAllText(Path.Combine(temp, "bunny.glb"), "");

            var first = ProjectAssetScanner.Scan(temp);
            var firstGuid = first.Root.Files.Single().MetaGuid;

            var second = ProjectAssetScanner.Scan(temp);
            var secondGuid = second.Root.Files.Single().MetaGuid;

            Assert.NotNull(firstGuid);
            Assert.Equal(firstGuid, secondGuid);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
