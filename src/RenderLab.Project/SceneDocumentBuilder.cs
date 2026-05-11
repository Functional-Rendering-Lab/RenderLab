using System.Collections.Immutable;
using System.Numerics;
using RenderLab.Assets;
using RenderLab.Functional;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Project;

/// <summary>
/// Pure inverse of <c>SceneLoader.Load</c>: takes the editable
/// <see cref="UiModel"/>, an <see cref="IAssetCatalog"/> for
/// resolving asset records, and the <see cref="SceneAssetSources"/>
/// the loader populated, and produces a fresh <see cref="SceneDocument"/>
/// suitable for writing through <c>ProjectIO.WriteScene</c>.
///
/// Materials are serialised inline (looked up via the catalog).
/// Meshes and textures are emitted in stable visit order — meshes in
/// drawable order, textures in materials' albedo-map order.
/// </summary>
public static class SceneDocumentBuilder
{
    public static Result<SceneDocument, SceneSaveError> From(
        UiModel ui, IAssetCatalog catalog, SceneAssetSources sources)
    {
        // Visit drawables, gathering meshes + materials in first-seen order.
        var meshOrder = new List<MeshId>();
        var meshIndex = new Dictionary<MeshId, int>();
        var materialOrder = new List<MaterialId>();
        var materialIndex = new Dictionary<MaterialId, int>();

        foreach (var d in ui.Drawables)
        {
            if (!meshIndex.ContainsKey(d.Mesh))
            {
                meshIndex[d.Mesh] = meshOrder.Count;
                meshOrder.Add(d.Mesh);
            }
            if (!materialIndex.ContainsKey(d.Material))
            {
                materialIndex[d.Material] = materialOrder.Count;
                materialOrder.Add(d.Material);
            }
        }

        // Mesh entries: every drawable-referenced mesh must have a known source.
        var meshDocs = ImmutableArray.CreateBuilder<MeshEntryDoc>(meshOrder.Count);
        foreach (var id in meshOrder)
        {
            if (!sources.MeshSources.TryGetValue(id, out var src))
                return Result.Error<SceneDocument, SceneSaveError>(
                    new SceneSaveError.MissingMeshSource(id, NameOrUnknown(catalog, id)));
            if (!catalog.TryGetMesh(id, out var asset))
                return Result.Error<SceneDocument, SceneSaveError>(
                    new SceneSaveError.UnknownAsset("mesh", id.Value));
            meshDocs.Add(new MeshEntryDoc(asset.Name, src));
        }

        // Texture entries: gather textures referenced by serialised materials'
        // albedo maps, in stable order, and require sources for each.
        var textureOrder = new List<TextureId>();
        var textureIndex = new Dictionary<TextureId, int>();

        foreach (var matId in materialOrder)
        {
            if (!catalog.TryGetMaterial(matId, out var matAsset))
                return Result.Error<SceneDocument, SceneSaveError>(
                    new SceneSaveError.UnknownAsset("material", matId.Value));
            if (matAsset is BlinnPhongMaterial bp && !bp.AlbedoMap.IsNone && !textureIndex.ContainsKey(bp.AlbedoMap))
            {
                textureIndex[bp.AlbedoMap] = textureOrder.Count;
                textureOrder.Add(bp.AlbedoMap);
            }
        }

        var textureDocs = ImmutableArray.CreateBuilder<TextureEntryDoc>(textureOrder.Count);
        foreach (var id in textureOrder)
        {
            if (!sources.TextureSources.TryGetValue(id, out var src))
                return Result.Error<SceneDocument, SceneSaveError>(
                    new SceneSaveError.MissingTextureSource(id, NameOrUnknown(catalog, id)));
            if (!catalog.TryGetTexture(id, out var asset))
                return Result.Error<SceneDocument, SceneSaveError>(
                    new SceneSaveError.UnknownAsset("texture", id.Value));
            textureDocs.Add(new TextureEntryDoc(asset.Name, src));
        }

        // Material entries (inline, in visit order; albedo refs rewritten to texture indices).
        var materialDocs = ImmutableArray.CreateBuilder<MaterialDoc>(materialOrder.Count);
        foreach (var matId in materialOrder)
        {
            var matAsset = catalog.GetMaterial(matId);
            switch (matAsset)
            {
                case BlinnPhongMaterial bp:
                    int? albedoMapIdx = bp.AlbedoMap.IsNone
                        ? null
                        : textureIndex[bp.AlbedoMap];
                    materialDocs.Add(new BlinnPhongMaterialDoc(
                        Name: bp.Name,
                        Albedo: ToArray3(bp.Albedo),
                        SpecularStrength: bp.SpecularStrength,
                        Shininess: bp.Shininess,
                        AlbedoMap: albedoMapIdx));
                    break;
                default:
                    return Result.Error<SceneDocument, SceneSaveError>(
                        new SceneSaveError.UnsupportedMaterialKind(matAsset.GetType().Name));
            }
        }

        var drawableDocs = new DrawableDoc[ui.Drawables.Length];
        for (int i = 0; i < ui.Drawables.Length; i++)
        {
            var d = ui.Drawables[i];
            drawableDocs[i] = new DrawableDoc(
                Name: d.Name,
                Mesh: meshIndex[d.Mesh],
                Material: materialIndex[d.Material],
                Transform: new TransformDoc(
                    Position: ToArray3(d.Transform.Position),
                    Rotation: ToArray4(d.Transform.Rotation),
                    Scale: d.Transform.Scale));
        }

        var lightDocs = new LightDoc[ui.Lights.Length];
        for (int i = 0; i < ui.Lights.Length; i++)
        {
            lightDocs[i] = ui.Lights[i] switch
            {
                PointLight p => new PointLightDoc(
                    ToArray3(p.Position), ToArray3(p.Color), p.Intensity.Value),
                DirectionalLight dl => new DirectionalLightDoc(
                    ToArray3(dl.Direction.Value), ToArray3(dl.Color), dl.Intensity.Value),
                var l => throw new InvalidOperationException($"unhandled light DU case {l.GetType().Name}"),
            };
        }

        const float radToDeg = 180f / MathF.PI;
        var camera = new CameraDoc(
            Position: ToArray3(ui.Camera.Position),
            YawDeg: ui.Camera.Yaw * radToDeg,
            PitchDeg: ui.Camera.Pitch * radToDeg,
            FovDeg: ui.Camera.FovRadians * radToDeg);

        var renderConfig = new RenderConfigDoc(
            Shading: ui.Shading switch
            {
                ShadingMode.Lambertian => "lambertian",
                ShadingMode.Phong      => "phong",
                ShadingMode.BlinnPhong => "blinnPhong",
                _                      => "blinnPhong",
            },
            LightingOnly: ui.LightingOnly,
            Viz: ui.Viz switch
            {
                VisualizationMode.Final    => "final",
                VisualizationMode.Position => "position",
                VisualizationMode.Normal   => "normal",
                VisualizationMode.Albedo   => "albedo",
                VisualizationMode.Depth    => "depth",
                VisualizationMode.HDR      => "hdr",
                _                          => "final",
            },
            ClearColor: ToArray3(ui.ClearColor));

        var doc = new SceneDocument(
            Version: 1,
            Camera: camera,
            Ambient: new AmbientDoc(ToArray3(ui.Ambient.Sky), ToArray3(ui.Ambient.Ground)),
            Lights: lightDocs,
            RenderConfig: renderConfig,
            Assets: new SceneAssetsDoc(
                Meshes: meshDocs.ToArray(),
                Textures: textureDocs.ToArray(),
                Materials: materialDocs.ToArray()),
            Drawables: drawableDocs);

        return Result.Ok<SceneDocument, SceneSaveError>(doc);
    }

    private static float[] ToArray3(Vector3 v) => [v.X, v.Y, v.Z];
    private static float[] ToArray4(Quaternion q) => [q.X, q.Y, q.Z, q.W];

    private static string NameOrUnknown(IAssetCatalog catalog, MeshId id) =>
        catalog.TryGetMesh(id, out var a) ? a.Name : $"#{id.Value}";

    private static string NameOrUnknown(IAssetCatalog catalog, TextureId id) =>
        catalog.TryGetTexture(id, out var a) ? a.Name : $"#{id.Value}";
}
