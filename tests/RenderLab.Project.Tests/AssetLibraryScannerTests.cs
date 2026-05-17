using RenderLab.Project;

namespace RenderLab.Project.Tests;

public class AssetLibraryScannerTests
{
    private static string MakeTemp() => Directory.CreateTempSubdirectory("renderlab-test-").FullName;

    [Fact]
    public void Scan_collects_mesh_and_texture_entries_keyed_by_guid()
    {
        var temp = MakeTemp();
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "assets"));
            File.WriteAllText(Path.Combine(temp, "assets", "bunny.glb"), "");
            File.WriteAllText(Path.Combine(temp, "assets", "stone.png"), "");

            var idx = ProjectAssetScanner.Scan(temp);
            var lib = AssetLibraryScanner.Scan(idx);

            Assert.Equal(2, lib.ByGuid.Count);
            Assert.Single(lib.EntriesOfKind(AssetKind.Mesh));
            Assert.Single(lib.EntriesOfKind(AssetKind.Texture));
            var mesh = (FileAssetEntry)lib.EntriesOfKind(AssetKind.Mesh).Single();
            Assert.Equal("bunny", mesh.Name);
            Assert.Equal("assets/bunny.glb", mesh.ProjectRelativePath);
            Assert.IsType<MeshImportSettings>(mesh.Import);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void Scan_reads_material_files_into_MaterialAssetEntry()
    {
        var temp = MakeTemp();
        try
        {
            var matDir = Path.Combine(temp, "assets", "materials");
            Directory.CreateDirectory(matDir);
            var matPath = Path.Combine(matDir, "stone.mat.json");
            AssetLibraryScanner.WriteMaterial(matPath, new MaterialFileDoc(
                Version: 1,
                Name: "Stone",
                Params: new MaterialParamsDoc([0.7f, 0.7f, 0.7f], 0.5f, 32f),
                AlbedoTex: RenderLab.Functional.Optional<AssetRef>.None));

            var idx = ProjectAssetScanner.Scan(temp);
            var lib = AssetLibraryScanner.Scan(idx);

            var mat = Assert.Single(lib.EntriesOfKind(AssetKind.Material));
            var m = Assert.IsType<MaterialAssetEntry>(mat);
            Assert.Equal("Stone", m.Name);
            Assert.Equal(0.5f, m.Params.SpecularStrength);
            Assert.True(m.AlbedoTex.IsNone);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void Scan_returns_empty_library_when_project_root_is_blank()
    {
        var lib = AssetLibraryScanner.Scan(ProjectAssetIndex.Empty(""));
        Assert.Same(AssetLibrary.Empty, lib);
    }
}
