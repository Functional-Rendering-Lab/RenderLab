using System.Text.Json;
using RenderLab.Project;

namespace RenderLab.Project.Tests;

public class SceneRoundTripTests
{
    private static SceneDocument SampleScene(Guid meshGuid, Guid matGuid) => new(
        Version: 2,
        Camera: new CameraDoc([0, 1, 5], 0f, 0f, 60f),
        Ambient: new AmbientDoc([0.5f, 0.6f, 0.8f], [0.2f, 0.2f, 0.2f]),
        Lights:
        [
            new PointLightDoc([2, 2, 2], [1, 1, 1], 5f),
            new DirectionalLightDoc([0, -1, 0], [1, 1, 1], 1f),
        ],
        RenderConfig: new RenderConfigDoc("blinnPhong", false, "final", [0.05f, 0.05f, 0.06f]),
        Drawables:
        [
            new DrawableDoc("Sphere",
                Mesh: new AssetRef(meshGuid),
                Material: new AssetRef(matGuid),
                new TransformDoc([0, 0, 0], [0, 0, 0, 1], 1f)),
        ]);

    [Fact]
    public void Scene_writes_then_reads_back_with_DUs_intact()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            ProjectIO.WriteManifest(temp, new ProjectManifest(1, "x", "deferred", "scenes/s.scene.json", ["scenes/s.scene.json"]));
            var mesh = Guid.NewGuid();
            var mat = Guid.NewGuid();
            var write = ProjectIO.WriteScene(temp, "scenes/s.scene.json", SampleScene(mesh, mat));
            Assert.True(write.IsOk);

            var read = ProjectIO.ReadScene(temp, "scenes/s.scene.json");
            Assert.True(read.IsOk);
            var doc = read.Match(ok: d => d, error: _ => null!);

            Assert.Equal(2, doc.Lights.Length);
            Assert.IsType<PointLightDoc>(doc.Lights[0]);
            Assert.IsType<DirectionalLightDoc>(doc.Lights[1]);
            Assert.Single(doc.Drawables);
            Assert.Equal(mesh, doc.Drawables[0].Mesh.Guid);
            Assert.Equal(mat, doc.Drawables[0].Material.Guid);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void ReadScene_upgrades_legacy_scene_files_in_place()
    {
        var temp = Directory.CreateTempSubdirectory("renderlab-test-").FullName;
        try
        {
            ProjectIO.WriteManifest(temp, new ProjectManifest(1, "x", "deferred", "scenes/s.scene.json", ["scenes/s.scene.json"]));
            // Drop a v1-shape JSON file directly so we exercise the
            // upgrade path on read.
            const string legacyJson = """
            {
              "version": 1,
              "camera":  { "position": [0,1,5], "yawDeg": 0, "pitchDeg": 0, "fovDeg": 60 },
              "ambient": { "sky": [0.4,0.5,0.7], "ground": [0.2,0.2,0.2] },
              "lights":  [ { "kind": "point", "position": [2,2,2], "color": [1,1,1], "intensity": 5 } ],
              "renderConfig": { "shading": "blinnPhong", "lightingOnly": false, "viz": "final", "clearColor": [0,0,0] },
              "assets": {
                "meshes":    [ { "name": "Sphere", "source": { "kind": "procedural", "generator": "sphere" } } ],
                "textures":  [],
                "materials": [ { "kind": "blinnPhong", "name": "Sphere", "albedo": [0.6,0.6,0.6], "specularStrength": 0.5, "shininess": 32, "albedoMap": null } ]
              },
              "drawables": [
                { "name": "Sphere", "mesh": 0, "material": 0, "transform": { "position": [0,0,0], "rotation": [0,0,0,1], "scale": 1 } }
              ]
            }
            """;
            var scenePath = Path.Combine(temp, "scenes", "s.scene.json");
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            File.WriteAllText(scenePath, legacyJson);

            var read = ProjectIO.ReadScene(temp, "scenes/s.scene.json");
            Assert.True(read.IsOk);
            var doc = read.Match(ok: d => d, error: _ => null!);

            // Upgraded shape: drawables hold AssetRefs.
            Assert.Single(doc.Drawables);
            Assert.NotEqual(Guid.Empty, doc.Drawables[0].Mesh.Guid);
            Assert.NotEqual(Guid.Empty, doc.Drawables[0].Material.Guid);

            // Upgrader wrote the new files
            Assert.True(Directory.Exists(Path.Combine(temp, "assets", "proc")));
            Assert.True(Directory.Exists(Path.Combine(temp, "assets", "materials")));

            // Reading again does not re-upgrade — the on-disk file is now v2.
            var read2 = ProjectIO.ReadScene(temp, "scenes/s.scene.json");
            Assert.True(read2.IsOk);
            var doc2 = read2.Match(ok: d => d, error: _ => null!);
            Assert.Equal(doc.Drawables[0].Mesh.Guid, doc2.Drawables[0].Mesh.Guid);
            Assert.Equal(doc.Drawables[0].Material.Guid, doc2.Drawables[0].Material.Guid);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
