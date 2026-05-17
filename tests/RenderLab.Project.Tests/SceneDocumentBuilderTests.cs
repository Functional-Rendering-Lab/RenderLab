using System.Collections.Immutable;
using System.Numerics;
using RenderLab.Assets;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Project.Tests;

/// <summary>
/// Round-trip tests for the AssetRef-based scene-document builder: feed a
/// runtime UiModel + SceneAssetSources (runtime-id → AssetRef) and verify
/// each drawable carries the right AssetRef in the resulting document.
/// </summary>
public class SceneDocumentBuilderTests
{
    [Fact]
    public void From_emits_drawables_with_AssetRef_for_mesh_and_material()
    {
        var (catalog, ui, sources, refs) = BuildSampleScene();
        var result = SceneDocumentBuilder.From(ui, catalog, sources);

        Assert.True(result.IsOk);
        var doc = result.Match(ok: d => d, error: _ => null!);

        Assert.Equal(2, doc.Drawables.Length);
        Assert.Equal(refs.SphereMesh, doc.Drawables[0].Mesh);
        Assert.Equal(refs.CubeMesh,   doc.Drawables[1].Mesh);
        Assert.Equal(refs.SphereMat,  doc.Drawables[0].Material);
        Assert.Equal(refs.CubeMat,    doc.Drawables[1].Material);
    }

    [Fact]
    public void From_then_round_trip_preserves_render_config_and_camera()
    {
        var (catalog, ui, sources, _) = BuildSampleScene();
        var built = SceneDocumentBuilder.From(ui, catalog, sources);
        var doc = built.Match(ok: d => d, error: _ => null!);

        Assert.Equal(ShadingMode.BlinnPhong, doc.RenderConfig.Shading);
        Assert.Equal(VisualizationMode.Final, doc.RenderConfig.Viz);
        Assert.False(doc.RenderConfig.LightingOnly);
        Assert.Equal(60f, doc.Camera.FovDeg, 3);

        Assert.Equal(2, doc.Lights.Length);
        Assert.IsType<PointLightDoc>(doc.Lights[0]);
        Assert.IsType<DirectionalLightDoc>(doc.Lights[1]);
    }

    [Fact]
    public void From_fails_with_MissingMeshSource_when_drawable_mesh_has_no_recorded_ref()
    {
        var (catalog, ui, _, _) = BuildSampleScene();
        var emptySources = SceneAssetSources.Empty;
        var result = SceneDocumentBuilder.From(ui, catalog, emptySources);
        Assert.True(result.IsError);
        var error = result.Match<SceneSaveError?>(_ => null, e => e);
        Assert.IsType<SceneSaveError.MissingMeshSource>(error);
    }

    // ─── Test fixtures ─────────────────────────────────────────────────

    private sealed record SampleRefs(AssetRef SphereMesh, AssetRef CubeMesh, AssetRef SphereMat, AssetRef CubeMat);

    private static (FakeCatalog Catalog, UiModel Ui, SceneAssetSources Sources, SampleRefs Refs) BuildSampleScene()
    {
        var catalog = new FakeCatalog();
        var sphereMesh = catalog.AddMesh("Sphere");
        var cubeMesh   = catalog.AddMesh("Cube");
        var sphereMat  = catalog.AddMaterial(new BlinnPhongMaterial(default, "SphereMat",
            new Vector3(0.6f, 0.6f, 0.6f), 0.5f, 32f, RenderLab.Functional.Optional<TextureId>.None));
        var cubeMat    = catalog.AddMaterial(new BlinnPhongMaterial(default, "CubeMat",
            new Vector3(0.6f, 0.6f, 0.6f), 0.5f, 32f, RenderLab.Functional.Optional<TextureId>.None));

        var refs = new SampleRefs(
            SphereMesh: new AssetRef(Guid.NewGuid()),
            CubeMesh:   new AssetRef(Guid.NewGuid()),
            SphereMat:  new AssetRef(Guid.NewGuid()),
            CubeMat:    new AssetRef(Guid.NewGuid()));

        var sources = SceneAssetSources.Empty
            .WithMesh(sphereMesh, refs.SphereMesh)
            .WithMesh(cubeMesh,   refs.CubeMesh)
            .WithMaterial(sphereMat, refs.SphereMat)
            .WithMaterial(cubeMat,   refs.CubeMat);

        var ui = UiModel.Default with
        {
            Lights = ImmutableArray.Create<Light>(
                new PointLight(new Vector3(2, 3, 2), Color01.UnsafeFrom(new Vector3(1, 1, 1)), Intensity.Of(5f)),
                new DirectionalLight(
                    Direction.UnsafeFromUnit(Vector3.Normalize(new Vector3(0, -1, 0))),
                    Color01.UnsafeFrom(new Vector3(1, 1, 1)), Intensity.Of(1f))),
            Drawables = ImmutableArray.Create(
                new EditableDrawable(Guid.NewGuid(), "Sphere", sphereMesh,
                    new Transform(Vector3.Zero, UnitQuaternion.Identity, PositiveScale.One), sphereMat),
                new EditableDrawable(Guid.NewGuid(), "Cube", cubeMesh,
                    new Transform(new Vector3(2.5f, 0, 0), UnitQuaternion.Identity, PositiveScale.One), cubeMat)),
            Camera = FreeCameraController.CreateDefault() with { Fov = Fov.FromDegrees(60f) },
        };
        return (catalog, ui, sources, refs);
    }

    private sealed class FakeCatalog : IAssetCatalog
    {
        private readonly Dictionary<int, MeshAsset> _meshes = new();
        private readonly Dictionary<int, TextureAsset> _textures = new();
        private readonly Dictionary<int, MaterialAsset> _materials = new();
        private int _nextMesh = 1;
        private int _nextTexture = 1;
        private int _nextMaterial = 1;

        public MeshId AddMesh(string name)
        {
            var id = new MeshId(_nextMesh++);
            _meshes[id.Value] = new MeshAsset(id, name, new MeshData([], []));
            return id;
        }

        public TextureId AddTexture(string name)
        {
            var id = new TextureId(_nextTexture++);
            _textures[id.Value] = new TextureAsset(id, name, 1, 1, TextureFormat.Rgba8Srgb, [255, 255, 255, 255]);
            return id;
        }

        public MaterialId AddMaterial(MaterialAsset proto)
        {
            var id = new MaterialId(_nextMaterial++);
            _materials[id.Value] = proto switch
            {
                BlinnPhongMaterial bp => bp with { Id = id },
                _ => throw new NotSupportedException(),
            };
            return id;
        }

        public MeshAsset GetMesh(MeshId id) => _meshes[id.Value];
        public bool TryGetMesh(MeshId id, out MeshAsset asset) => _meshes.TryGetValue(id.Value, out asset!);
        public IEnumerable<MeshAsset> AllMeshes => _meshes.Values;

        public TextureAsset GetTexture(TextureId id) => _textures[id.Value];
        public bool TryGetTexture(TextureId id, out TextureAsset asset) => _textures.TryGetValue(id.Value, out asset!);
        public IEnumerable<TextureAsset> AllTextures => _textures.Values;

        public MaterialAsset GetMaterial(MaterialId id) => _materials[id.Value];
        public bool TryGetMaterial(MaterialId id, out MaterialAsset asset) => _materials.TryGetValue(id.Value, out asset!);
        public IEnumerable<MaterialAsset> AllMaterials => _materials.Values;

        public bool IsBuiltin(MeshId id) => false;
        public bool IsBuiltin(TextureId id) => false;
        public bool IsBuiltin(MaterialId id) => false;
    }
}
