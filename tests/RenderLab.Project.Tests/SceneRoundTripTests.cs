using System.Text.Json;
using RenderLab.Project;

namespace RenderLab.Project.Tests;

public class SceneRoundTripTests
{
    private static SceneDocument SampleScene() => new(
        Version: 1,
        Camera: new CameraDoc([0, 1, 5], [0, 0, 0], 60f),
        Ambient: new AmbientDoc([0.5f, 0.6f, 0.8f], [0.2f, 0.2f, 0.2f]),
        Lights:
        [
            new PointLightDoc([2, 2, 2], [1, 1, 1], 5f),
            new DirectionalLightDoc([0, -1, 0], [1, 1, 1], 1f),
        ],
        RenderConfig: new RenderConfigDoc("blinnPhong", false, "final", [0.05f, 0.05f, 0.06f]),
        Assets: new SceneAssetsDoc(
            Meshes:
            [
                new MeshEntryDoc("Sphere", new ProceduralSourceDoc("sphere", new() { ["stacks"] = JsonDocument.Parse("32").RootElement })),
                new MeshEntryDoc("Box", new FileSourceDoc("assets/box.glb#mesh0")),
            ],
            Textures:
            [
                new TextureEntryDoc("BoxAlbedo", new FileSourceDoc("assets/box.glb#image0")),
            ],
            Materials:
            [
                new BlinnPhongMaterialDoc("Sphere", [0.6f, 0.6f, 0.6f], 0.5f, 32f, AlbedoMap: null),
                new BlinnPhongMaterialDoc("Box", [1, 1, 1], 0.5f, 32f, AlbedoMap: 0),
            ]),
        Drawables:
        [
            new DrawableDoc("Sphere", Mesh: 0, Material: 0, new TransformDoc([0, 0, 0], [0, 0, 0, 1], 1f)),
            new DrawableDoc("Box",    Mesh: 1, Material: 1, new TransformDoc([2.5f, 0, 0], [0, 0, 0, 1], 1f)),
        ]);

    [Fact]
    public void Scene_writes_then_reads_back_with_DUs_intact()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            ProjectIO.WriteManifest(temp, new ProjectManifest(1, "x", "deferred", "scenes/s.scene.json", ["scenes/s.scene.json"]));
            var write = ProjectIO.WriteScene(temp, "scenes/s.scene.json", SampleScene());
            Assert.True(write.IsOk);

            var read = ProjectIO.ReadScene(temp, "scenes/s.scene.json");
            Assert.True(read.IsOk);
            var doc = read.Match(ok: d => d, error: _ => null!);

            Assert.Equal(2, doc.Lights.Length);
            Assert.IsType<PointLightDoc>(doc.Lights[0]);
            Assert.IsType<DirectionalLightDoc>(doc.Lights[1]);
            Assert.Equal(2, doc.Assets.Meshes.Length);
            Assert.IsType<ProceduralSourceDoc>(doc.Assets.Meshes[0].Source);
            Assert.IsType<FileSourceDoc>(doc.Assets.Meshes[1].Source);
            Assert.Equal(2, doc.Assets.Materials.Length);
            Assert.Equal(0, ((BlinnPhongMaterialDoc)doc.Assets.Materials[1]).AlbedoMap);
            Assert.Null(((BlinnPhongMaterialDoc)doc.Assets.Materials[0]).AlbedoMap);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ReadScene_rejects_dangling_drawable_mesh_index()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            ProjectIO.WriteManifest(temp, new ProjectManifest(1, "x", "deferred", "scenes/s.scene.json", ["scenes/s.scene.json"]));

            var bad = SampleScene() with
            {
                Drawables = [new DrawableDoc("X", Mesh: 99, Material: 0, new TransformDoc([0, 0, 0], [0, 0, 0, 1], 1f))],
            };
            ProjectIO.WriteScene(temp, "scenes/s.scene.json", bad);
            var read = ProjectIO.ReadScene(temp, "scenes/s.scene.json");
            Assert.True(read.IsError);
            Assert.IsType<ProjectError.DanglingIndex>(read.Match<ProjectError?>(_ => null, e => e));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ReadScene_rejects_dangling_albedoMap_index()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            ProjectIO.WriteManifest(temp, new ProjectManifest(1, "x", "deferred", "scenes/s.scene.json", ["scenes/s.scene.json"]));

            var bad = SampleScene() with
            {
                Assets = SampleScene().Assets with
                {
                    Materials = [new BlinnPhongMaterialDoc("X", [1, 1, 1], 0.5f, 32f, AlbedoMap: 99)],
                },
                Drawables = [],
            };
            ProjectIO.WriteScene(temp, "scenes/s.scene.json", bad);
            var read = ProjectIO.ReadScene(temp, "scenes/s.scene.json");
            Assert.True(read.IsError);
            Assert.IsType<ProjectError.DanglingIndex>(read.Match<ProjectError?>(_ => null, e => e));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
