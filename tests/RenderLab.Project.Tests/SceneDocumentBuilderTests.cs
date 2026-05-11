using System.Collections.Immutable;
using System.Numerics;
using RenderLab.Assets;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Project.Tests;

/// <summary>
/// Round-trip tests for the pure side of M6.2: build a SceneDocument from a
/// runtime UiModel + asset catalog + recorded sources, and verify the
/// resulting document references assets by index in stable visit order and
/// preserves every editable field.
/// </summary>
public class SceneDocumentBuilderTests
{
    [Fact]
    public void From_emits_meshes_textures_materials_in_visit_order()
    {
        var (catalog, ui, sources) = BuildSampleScene();
        var result = SceneDocumentBuilder.From(ui, catalog, sources);

        Assert.True(result.IsOk);
        var doc = result.Match(ok: d => d, error: _ => null!);

        // Two distinct meshes referenced by drawables → two mesh entries.
        Assert.Equal(2, doc.Assets.Meshes.Length);
        Assert.Equal("Sphere", doc.Assets.Meshes[0].Name);
        Assert.Equal("Cube",   doc.Assets.Meshes[1].Name);

        // One texture referenced by Cube material's albedo map.
        Assert.Single(doc.Assets.Textures);
        Assert.Equal("Checker", doc.Assets.Textures[0].Name);

        // Materials in drawable visit order; albedoMap rewritten to texture index 0.
        Assert.Equal(2, doc.Assets.Materials.Length);
        Assert.Null(((BlinnPhongMaterialDoc)doc.Assets.Materials[0]).AlbedoMap);
        Assert.Equal(0, ((BlinnPhongMaterialDoc)doc.Assets.Materials[1]).AlbedoMap);
    }

    [Fact]
    public void From_then_round_trip_preserves_drawables_and_render_config()
    {
        var (catalog, ui, sources) = BuildSampleScene();
        var built = SceneDocumentBuilder.From(ui, catalog, sources);
        Assert.True(built.IsOk);
        var doc = built.Match(ok: d => d, error: _ => null!);

        // Drawable indices remap to the document's per-scene asset arrays.
        Assert.Equal(2, doc.Drawables.Length);
        Assert.Equal(0, doc.Drawables[0].Mesh);
        Assert.Equal(1, doc.Drawables[1].Mesh);
        Assert.Equal(0, doc.Drawables[0].Material);
        Assert.Equal(1, doc.Drawables[1].Material);

        // RenderConfig string encoding survives.
        Assert.Equal("blinnPhong", doc.RenderConfig.Shading);
        Assert.Equal("final",      doc.RenderConfig.Viz);
        Assert.False(doc.RenderConfig.LightingOnly);

        // Camera FOV converts radians → degrees on save.
        Assert.Equal(60f, doc.Camera.FovDeg, 3);

        // Lights: point + directional with their kind discriminator preserved.
        Assert.Equal(2, doc.Lights.Length);
        Assert.IsType<PointLightDoc>(doc.Lights[0]);
        Assert.IsType<DirectionalLightDoc>(doc.Lights[1]);
    }

    [Fact]
    public void From_fails_with_MissingMeshSource_when_drawable_references_untracked_mesh()
    {
        var (catalog, ui, _) = BuildSampleScene();
        // Sources without the meshes that drawables reference → save must
        // refuse rather than silently dropping the entry.
        var emptySources = SceneAssetSources.Empty;

        var result = SceneDocumentBuilder.From(ui, catalog, emptySources);

        Assert.True(result.IsError);
        var error = result.Match<SceneSaveError?>(_ => null, e => e);
        Assert.IsType<SceneSaveError.MissingMeshSource>(error);
    }

    [Fact]
    public void From_does_not_serialise_orphan_textures_with_no_material_reference()
    {
        // Catalog has a stray texture nobody points to. The builder visits
        // drawables → materials → albedo maps, so unreferenced textures
        // never reach the document.
        var catalog = new FakeCatalog();
        var meshId = catalog.AddMesh("Sphere");
        var orphanTex = catalog.AddTexture("Stray");
        var matId = catalog.AddMaterial(new BlinnPhongMaterial(default, "Mat",
            new Vector3(1, 1, 1), 0.5f, 32f, TextureId.None));

        var sources = SceneAssetSources.Empty
            .WithMesh(meshId, new ProceduralSourceDoc("sphere", null))
            .WithTexture(orphanTex, new ProceduralSourceDoc("checker", null));

        var ui = UiModel.Default with
        {
            Drawables = ImmutableArray.Create(new EditableDrawable(
                Guid.NewGuid(), "Sphere", meshId,
                new Transform(Vector3.Zero, Quaternion.Identity, 1f), matId)),
        };

        var result = SceneDocumentBuilder.From(ui, catalog, sources);
        Assert.True(result.IsOk);
        var doc = result.Match(ok: d => d, error: _ => null!);

        Assert.Empty(doc.Assets.Textures);
    }

    // ─── Test fixtures ─────────────────────────────────────────────────

    private static (FakeCatalog Catalog, UiModel Ui, SceneAssetSources Sources) BuildSampleScene()
    {
        var catalog = new FakeCatalog();
        var sphereMesh = catalog.AddMesh("Sphere");
        var cubeMesh   = catalog.AddMesh("Cube");
        var checkerTex = catalog.AddTexture("Checker");
        var sphereMat  = catalog.AddMaterial(new BlinnPhongMaterial(default, "SphereMat",
            new Vector3(0.6f, 0.6f, 0.6f), 0.5f, 32f, TextureId.None));
        var cubeMat    = catalog.AddMaterial(new BlinnPhongMaterial(default, "CubeMat",
            new Vector3(0.6f, 0.6f, 0.6f), 0.5f, 32f, checkerTex));

        var sources = SceneAssetSources.Empty
            .WithMesh(sphereMesh, new ProceduralSourceDoc("sphere", null))
            .WithMesh(cubeMesh,   new ProceduralSourceDoc("cube", null))
            .WithTexture(checkerTex, new ProceduralSourceDoc("checker", null));

        var ui = UiModel.Default with
        {
            Lights = ImmutableArray.Create<Light>(
                new PointLight(new Vector3(2, 3, 2), new Vector3(1, 1, 1), Intensity.Of(5f)),
                new DirectionalLight(
                    Direction.UnsafeFromUnit(Vector3.Normalize(new Vector3(0, -1, 0))),
                    new Vector3(1, 1, 1), Intensity.Of(1f))),
            Drawables = ImmutableArray.Create(
                new EditableDrawable(Guid.NewGuid(), "Sphere", sphereMesh,
                    new Transform(Vector3.Zero, Quaternion.Identity, 1f), sphereMat),
                new EditableDrawable(Guid.NewGuid(), "Cube", cubeMesh,
                    new Transform(new Vector3(2.5f, 0, 0), Quaternion.Identity, 1f), cubeMat)),
            Camera = FreeCameraController.CreateDefault() with { FovRadians = 60f * (MathF.PI / 180f) },
        };
        return (catalog, ui, sources);
    }

    /// <summary>
    /// Hand-rolled <see cref="IAssetCatalog"/> stand-in: holds dictionaries
    /// keyed by id and re-issues sequential ids on each <c>Add*</c>. Lets the
    /// pure builder be exercised without booting a GPU registry.
    /// </summary>
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
