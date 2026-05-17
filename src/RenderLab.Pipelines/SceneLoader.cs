using System.Collections.Immutable;
using System.Numerics;
using RenderLab.Assets;
using RenderLab.Functional;
using RenderLab.Gpu.Assets;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Pipelines;

/// <summary>
/// Bootstraps a runtime scene from a pure <see cref="SceneDocument"/> by
/// walking each drawable's <see cref="AssetRef"/> through a
/// <see cref="SceneAssetResolver"/>. The resolver lazily registers
/// previously-unseen refs into the <see cref="AssetRegistry"/> and
/// returns cached ids for refs it has already resolved, so opening the
/// same scene twice (or two scenes that share assets) doesn't re-upload
/// to the GPU.
///
/// Tracks the <see cref="AssetRef"/> each registered id came from in a
/// <see cref="SceneAssetSources"/> map so the inverse
/// <c>SceneDocumentBuilder.From</c> can round-trip back to disk on save.
/// The map is rebuilt fresh for each loaded scene from the refs this
/// scene actually references.
/// </summary>
public static class SceneLoader
{
    public sealed record LoadedScene(UiModel Ui, SceneAssetSources Sources);

    public static Result<LoadedScene, SceneLoadError> Load(
        SceneDocument doc,
        SceneAssetResolver resolver)
    {
        var sources = SceneAssetSources.Empty;

        var drawables = ImmutableArray.CreateBuilder<EditableDrawable>(doc.Drawables.Length);
        foreach (var d in doc.Drawables)
        {
            var meshR = resolver.ResolveMesh(d.Mesh);
            if (meshR.IsError) return Result.Error<LoadedScene, SceneLoadError>(meshR.Match<SceneLoadError>(_ => null!, e => e));
            var meshId = meshR.Match(ok: x => x, error: _ => default);
            sources = sources.WithMesh(meshId, d.Mesh);

            var matR = resolver.ResolveMaterial(d.Material);
            if (matR.IsError) return Result.Error<LoadedScene, SceneLoadError>(matR.Match<SceneLoadError>(_ => null!, e => e));
            var matId = matR.Match(ok: x => x, error: _ => default);
            sources = sources.WithMaterial(matId, d.Material);

            // Record the material's texture ref → id mapping too, so the
            // Asset Browser material editor can map a freshly-edited
            // AlbedoTex AssetRef back to a live TextureId. ResolveMaterial
            // already resolved any AlbedoTex above, so this is a cache
            // hit, not a re-upload.
            if (resolver.LibraryEntry(d.Material.Guid) is MaterialAssetEntry m && m.AlbedoTex.IsSome)
            {
                var tr = m.AlbedoTex.Match(some: x => x, none: () => default!);
                var tres = resolver.ResolveTexture(tr);
                if (tres.IsOk)
                    sources = sources.WithTexture(tres.Match(ok: x => x, error: _ => default), tr);
            }

            drawables.Add(new EditableDrawable(
                LocalId: Guid.NewGuid(),
                Name: d.Name,
                Mesh: meshId,
                Transform: new Transform(
                    ToVec3(d.Transform.Position),
                    UnitQuaternion.UnsafeFromUnit(ToQuat(d.Transform.Rotation)),
                    PositiveScale.UnsafeFrom(d.Transform.Scale > 0f ? d.Transform.Scale : 1f)),
                Material: matId));
        }

        var lights = ImmutableArray.CreateBuilder<Light>(doc.Lights.Length);
        foreach (var l in doc.Lights)
        {
            lights.Add(l switch
            {
                PointLightDoc p => (Light)new PointLight(ToVec3(p.Position), Color01.UnsafeFrom(ToVec3(p.Color)), Intensity.Of(p.Intensity)),
                DirectionalLightDoc d2 => new DirectionalLight(
                    Direction.UnsafeFromUnit(Vector3.Normalize(ToVec3(d2.Direction))),
                    Color01.UnsafeFrom(ToVec3(d2.Color)),
                    Intensity.Of(d2.Intensity)),
                _ => throw new InvalidOperationException($"Unknown light DU case {l.GetType().Name}"),
            });
        }

        var camRad = MathF.PI / 180f;
        var camera = FreeCameraController.CreateDefault() with
        {
            Position = ToVec3(doc.Camera.Position),
            Yaw = doc.Camera.YawDeg * camRad,
            Pitch = doc.Camera.PitchDeg * camRad,
            Fov = Fov.UnsafeFromRadians(doc.Camera.FovDeg * camRad),
        };

        var ui = UiModel.Default with
        {
            Camera = camera,
            Lights = lights.ToImmutable(),
            Ambient = new HemisphericAmbient(
                Color01.UnsafeFrom(ToVec3(doc.Ambient.Sky)),
                Color01.UnsafeFrom(ToVec3(doc.Ambient.Ground))),
            Drawables = drawables.ToImmutable(),
            Selection = drawables.Count > 0
                ? new Selection.Drawable(drawables[0].LocalId)
                : Selection.Empty,
            Shading = doc.RenderConfig.Shading,
            LightingOnly = doc.RenderConfig.LightingOnly,
            Viz = doc.RenderConfig.Viz,
            ClearColor = Color01.UnsafeFrom(ToVec3(doc.RenderConfig.ClearColor)),
            Background = doc.RenderConfig.Background ?? BackgroundMode.SolidColor,
        };
        return Result.Ok<LoadedScene, SceneLoadError>(new LoadedScene(ui, sources));
    }

    private static Vector3 ToVec3(float[] a) =>
        a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

    private static Quaternion ToQuat(float[] a) =>
        a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.Identity;
}
