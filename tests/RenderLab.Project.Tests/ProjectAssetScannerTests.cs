using RenderLab.Project;

namespace RenderLab.Project.Tests;

public class ProjectAssetScannerTests
{
    private static string MakeTemp() => Directory.CreateTempSubdirectory("renderlab-test-").FullName;

    [Fact]
    public void Scan_returns_empty_index_root_when_folder_is_empty()
    {
        var temp = MakeTemp();
        try
        {
            var idx = ProjectAssetScanner.Scan(temp);
            Assert.Equal(temp, idx.ProjectRoot);
            Assert.Empty(idx.Root.Subfolders);
            Assert.Empty(idx.Root.Files);
            Assert.Equal("", idx.Root.ProjectRelativePath);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void Scan_recursively_lists_folders_then_files_alphabetical()
    {
        var temp = MakeTemp();
        try
        {
            File.WriteAllText(Path.Combine(temp, "zeta.txt"), "");
            File.WriteAllText(Path.Combine(temp, "alpha.md"), "");
            Directory.CreateDirectory(Path.Combine(temp, "scenes"));
            Directory.CreateDirectory(Path.Combine(temp, "assets"));
            File.WriteAllText(Path.Combine(temp, "assets", "box.glb"), "");

            var idx = ProjectAssetScanner.Scan(temp);

            Assert.Equal(new[] { "alpha.md", "zeta.txt" }, idx.Root.Files.Select(f => f.Name).ToArray());
            Assert.Equal(new[] { "assets", "scenes" }, idx.Root.Subfolders.Select(f => f.Name).ToArray());

            var assets = idx.Root.Subfolders.First(s => s.Name == "assets");
            Assert.Single(assets.Files);
            Assert.Equal("box.glb", assets.Files[0].Name);
            Assert.Equal(ProjectFileKind.GltfModel, assets.Files[0].Kind);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Theory]
    [InlineData("project.json", ProjectFileKind.Manifest)]
    [InlineData("main.scene.json", ProjectFileKind.Scene)]
    [InlineData("box.glb", ProjectFileKind.GltfModel)]
    [InlineData("box.gltf", ProjectFileKind.GltfModel)]
    [InlineData("tex.png", ProjectFileKind.Image)]
    [InlineData("tex.jpg", ProjectFileKind.Image)]
    [InlineData("tex.jpeg", ProjectFileKind.Image)]
    [InlineData("tex.tga", ProjectFileKind.Image)]
    [InlineData("tex.bmp", ProjectFileKind.Image)]
    [InlineData("notes.md", ProjectFileKind.Text)]
    [InlineData("lit.frag", ProjectFileKind.Shader)]
    [InlineData("meta.json", ProjectFileKind.Json)]
    [InlineData("weird.xyz", ProjectFileKind.Other)]
    public void Scan_classifies_known_extensions(string name, ProjectFileKind expected)
    {
        Assert.Equal(expected, ProjectAssetScanner.Classify(name));
    }

    [Fact]
    public void Scan_skips_bin_obj_dotfolders()
    {
        var temp = MakeTemp();
        try
        {
            foreach (var d in new[] { "bin", "obj", ".git", ".idea" })
            {
                Directory.CreateDirectory(Path.Combine(temp, d));
                File.WriteAllText(Path.Combine(temp, d, "foo.txt"), "");
            }
            File.WriteAllText(Path.Combine(temp, "keep.txt"), "");

            var idx = ProjectAssetScanner.Scan(temp);
            Assert.Empty(idx.Root.Subfolders);
            Assert.Single(idx.Root.Files);
            Assert.Equal("keep.txt", idx.Root.Files[0].Name);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void Scan_records_project_relative_paths_with_forward_slashes()
    {
        var temp = MakeTemp();
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "assets", "sub"));
            File.WriteAllText(Path.Combine(temp, "assets", "sub", "x.png"), "");

            var idx = ProjectAssetScanner.Scan(temp);
            var assets = idx.Root.Subfolders.Single();
            var sub = assets.Subfolders.Single();
            var file = sub.Files.Single();

            Assert.Equal("assets", assets.ProjectRelativePath);
            Assert.Equal("assets/sub", sub.ProjectRelativePath);
            Assert.Equal("assets/sub/x.png", file.ProjectRelativePath);
            Assert.DoesNotContain('\\', file.ProjectRelativePath);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void Scan_returns_empty_index_when_root_does_not_exist()
    {
        var fake = Path.Combine(Path.GetTempPath(), "renderlab-does-not-exist-" + Guid.NewGuid().ToString("N"));
        var idx = ProjectAssetScanner.Scan(fake);
        Assert.Equal(fake, idx.ProjectRoot);
        Assert.Empty(idx.Root.Subfolders);
        Assert.Empty(idx.Root.Files);
        Assert.Equal(DateTime.MinValue, idx.ScannedAtUtc);
    }
}
