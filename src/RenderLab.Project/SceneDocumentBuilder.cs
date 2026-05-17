using System.Numerics;
using RenderLab.Assets;
using RenderLab.Functional;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Project;

/// <summary>
/// Pure inverse of <c>SceneLoader.Load</c>: takes the editable
/// <see cref="UiModel"/> + the runtime-id → <see cref="AssetRef"/> map the
/// loader populated, and produces a fresh <see cref="SceneDocument"/>
/// suitable for writing through <c>ProjectIO.WriteScene</c>.
///
/// Asset definitions are NOT embedded in the scene — drawables reference
/// meshes and materials via stable <see cref="AssetRef"/> resolved against
/// the project's <see cref="AssetLibrary"/>.
/// </summary>
public static class SceneDocumentBuilder
{
    public static Result<SceneDocument, SceneSaveError> From(
        UiModel ui, IAssetCatalog catalog, SceneAssetSources sources)
    {
        var drawables = new DrawableDoc[ui.Drawables.Length];
        for (int i = 0; i < ui.Drawables.Length; i++)
        {
            var d = ui.Drawables[i];
            if (!sources.MeshSources.TryGetValue(d.Mesh, out var meshRef))
                return Result.Error<SceneDocument, SceneSaveError>(
                    new SceneSaveError.MissingMeshSource(d.Mesh, NameOrUnknown(catalog, d.Mesh)));
            if (!sources.MaterialSources.TryGetValue(d.Material, out var matRef))
                return Result.Error<SceneDocument, SceneSaveError>(
                    new SceneSaveError.MissingMaterialSource(d.Material, NameOrUnknown(catalog, d.Material)));

            drawables[i] = new DrawableDoc(
                Name: d.Name,
                Mesh: meshRef,
                Material: matRef,
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
            FovDeg: ui.Camera.Fov * radToDeg);

        var renderConfig = new RenderConfigDoc(
            Shading: ui.Shading,
            LightingOnly: ui.LightingOnly,
            Viz: ui.Viz,
            ClearColor: ToArray3(ui.ClearColor),
            Background: ui.Background);

        var doc = new SceneDocument(
            Version: 2,
            Camera: camera,
            Ambient: new AmbientDoc(ToArray3(ui.Ambient.Sky), ToArray3(ui.Ambient.Ground)),
            Lights: lightDocs,
            RenderConfig: renderConfig,
            Drawables: drawables);

        return Result.Ok<SceneDocument, SceneSaveError>(doc);
    }

    private static float[] ToArray3(Vector3 v) => [v.X, v.Y, v.Z];
    private static float[] ToArray4(Quaternion q) => [q.X, q.Y, q.Z, q.W];

    private static string NameOrUnknown(IAssetCatalog catalog, MeshId id) =>
        catalog.TryGetMesh(id, out var a) ? a.Name : $"#{id.Value}";
    private static string NameOrUnknown(IAssetCatalog catalog, MaterialId id) =>
        catalog.TryGetMaterial(id, out var a) ? a.Name : $"#{id.Value}";
}
