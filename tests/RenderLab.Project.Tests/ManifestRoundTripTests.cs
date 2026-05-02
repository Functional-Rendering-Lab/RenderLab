using RenderLab.Project;

namespace RenderLab.Project.Tests;

public class ManifestRoundTripTests
{
    [Fact]
    public void Manifest_writes_then_reads_back_unchanged()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            var original = new ProjectManifest(
                Version: 1,
                Name: "Deferred Lab",
                Pipeline: "deferred",
                DefaultScene: "scenes/main.scene.json",
                Scenes: ["scenes/main.scene.json", "scenes/materials-test.scene.json"]);

            ProjectIO.WriteManifest(temp, original);
            var read = ProjectIO.ReadManifest(temp);

            Assert.True(read.IsOk);
            var roundTripped = read.Match(ok: m => m, error: _ => null!);
            Assert.Equal(original.Version, roundTripped.Version);
            Assert.Equal(original.Name, roundTripped.Name);
            Assert.Equal(original.Pipeline, roundTripped.Pipeline);
            Assert.Equal(original.DefaultScene, roundTripped.DefaultScene);
            Assert.Equal(original.Scenes, roundTripped.Scenes);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ReadManifest_rejects_project_root_without_assets_folder()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            // WriteManifest creates assets/ for us; remove it to simulate a malformed project.
            ProjectIO.WriteManifest(temp, new ProjectManifest(1, "x", "deferred", "", []));
            Directory.Delete(Path.Combine(temp, ProjectIO.AssetsFolderName));

            var result = ProjectIO.ReadManifest(temp);
            Assert.True(result.IsError);
            Assert.IsType<ProjectError.MissingProjectComponent>(result.Match<ProjectError?>(_ => null, e => e));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ReadManifest_returns_FileNotFound_when_absent()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, ProjectIO.AssetsFolderName));
            var result = ProjectIO.ReadManifest(temp);
            Assert.True(result.IsError);
            Assert.IsType<ProjectError.FileNotFound>(result.Match<ProjectError?>(_ => null, e => e));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
