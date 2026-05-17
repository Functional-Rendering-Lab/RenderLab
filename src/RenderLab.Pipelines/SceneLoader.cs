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
        SceneAssetResolver resolver) =>
        BuildDrawables(resolver, doc.Drawables)
            .Bind(ds => BuildLights(doc.Lights)
                .Map(ls => (ds.Drawables, ds.Sources, Lights: ls)))
            .Bind(t => BuildCamera(doc.Camera)
                .Map(cam => (t.Drawables, t.Sources, t.Lights, Camera: cam)))
            .Bind(t => BuildAmbient(doc.Ambient)
                .Map(amb => (t.Drawables, t.Sources, t.Lights, t.Camera, Ambient: amb)))
            .Bind(t => BuildClearColor(doc.RenderConfig.ClearColor)
                .Map(cc => Assemble(t.Drawables, t.Sources, t.Lights, t.Camera, t.Ambient, cc, doc.RenderConfig)));

    private static Result<(ImmutableArray<EditableDrawable> Drawables, SceneAssetSources Sources), SceneLoadError>
        BuildDrawables(SceneAssetResolver resolver, DrawableDoc[] docs)
    {
        var sources = SceneAssetSources.Empty;
        var drawables = ImmutableArray.CreateBuilder<EditableDrawable>(docs.Length);

        foreach (var d in docs)
        {
            var meshR = resolver.ResolveMesh(d.Mesh);
            if (meshR.IsError)
                return Result.Error<(ImmutableArray<EditableDrawable>, SceneAssetSources), SceneLoadError>(
                    meshR.Match(_ => default!, e => e));
            var meshId = meshR.Match(x => x, _ => default);
            sources = sources.WithMesh(meshId, d.Mesh);

            var matR = resolver.ResolveMaterial(d.Material);
            if (matR.IsError)
                return Result.Error<(ImmutableArray<EditableDrawable>, SceneAssetSources), SceneLoadError>(
                    matR.Match(_ => default!, e => e));
            var matId = matR.Match(x => x, _ => default);
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
                    sources = sources.WithTexture(tres.Match(x => x, _ => default), tr);
            }

            var transformR = BuildTransform(d.Transform);
            if (transformR.IsError)
                return Result.Error<(ImmutableArray<EditableDrawable>, SceneAssetSources), SceneLoadError>(
                    transformR.Match(_ => default!, e => e));

            drawables.Add(new EditableDrawable(
                LocalId: Guid.NewGuid(),
                Name: d.Name,
                Mesh: meshId,
                Transform: transformR.Match(x => x, _ => default!),
                Material: matId));
        }

        return Result.Ok<(ImmutableArray<EditableDrawable>, SceneAssetSources), SceneLoadError>(
            (drawables.ToImmutable(), sources));
    }

    private static Result<Transform, SceneLoadError> BuildTransform(TransformDoc t)
    {
        var rot = UnitQuaternion.Create(ToQuat(t.Rotation))
            .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("transform.rotation", e.Message));
        if (rot.IsError) return Result.Error<Transform, SceneLoadError>(rot.Match(_ => default!, e => e));

        var scale = PositiveScale.Of(t.Scale)
            .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("transform.scale", e.Message));
        if (scale.IsError) return Result.Error<Transform, SceneLoadError>(scale.Match(_ => default!, e => e));

        return Result.Ok<Transform, SceneLoadError>(new Transform(
            ToVec3(t.Position),
            rot.Match(x => x, _ => default),
            scale.Match(x => x, _ => default)));
    }

    private static Result<ImmutableArray<Light>, SceneLoadError> BuildLights(LightDoc[] docs) =>
        Result.Traverse<LightDoc, Light, SceneLoadError>(docs, BuildLight);

    private static Result<Light, SceneLoadError> BuildLight(LightDoc l)
    {
        switch (l)
        {
            case PointLightDoc p:
                return Color01.Of(ToVec3(p.Color))
                    .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("light.color", e.Message))
                    .Bind(color => Intensity.Of(p.Intensity)
                        .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("light.intensity", e.Message))
                        .Map(intensity => (Light)new PointLight(ToVec3(p.Position), color, intensity)));
            case DirectionalLightDoc d:
                return Direction.Create(ToVec3(d.Direction))
                    .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("light.direction", e.Message))
                    .Bind(dir => Color01.Of(ToVec3(d.Color))
                        .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("light.color", e.Message))
                        .Bind(color => Intensity.Of(d.Intensity)
                            .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("light.intensity", e.Message))
                            .Map(intensity => (Light)new DirectionalLight(dir, color, intensity))));
            default:
                return Result.Error<Light, SceneLoadError>(new SceneLoadError.InvalidLightKind(l.GetType().Name));
        }
    }

    private static Result<FreeCameraState, SceneLoadError> BuildCamera(CameraDoc c)
    {
        var camRad = MathF.PI / 180f;
        var fov = Fov.FromRadians(c.FovDeg * camRad)
            .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("camera.fovDeg", e.Message));
        return fov.Map(f => FreeCameraController.CreateDefault() with
        {
            Position = ToVec3(c.Position),
            Yaw = c.YawDeg * camRad,
            Pitch = c.PitchDeg * camRad,
            Fov = f,
        });
    }

    private static Result<HemisphericAmbient, SceneLoadError> BuildAmbient(AmbientDoc a) =>
        Color01.Of(ToVec3(a.Sky))
            .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("ambient.sky", e.Message))
            .Bind(sky => Color01.Of(ToVec3(a.Ground))
                .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("ambient.ground", e.Message))
                .Map(ground => new HemisphericAmbient(sky, ground)));

    private static Result<Color01, SceneLoadError> BuildClearColor(float[] arr) =>
        Color01.Of(ToVec3(arr))
            .MapError<SceneLoadError>(e => new SceneLoadError.InvalidValue("renderConfig.clearColor", e.Message));

    private static LoadedScene Assemble(
        ImmutableArray<EditableDrawable> drawables,
        SceneAssetSources sources,
        ImmutableArray<Light> lights,
        FreeCameraState camera,
        HemisphericAmbient ambient,
        Color01 clearColor,
        RenderConfigDoc rc)
    {
        var ui = UiModel.Default with
        {
            Camera = camera,
            Lights = lights,
            Ambient = ambient,
            Drawables = drawables,
            Selection = drawables.Length > 0
                ? new Selection.Drawable(drawables[0].LocalId)
                : Selection.Empty,
            Shading = rc.Shading,
            LightingOnly = rc.LightingOnly,
            Viz = rc.Viz,
            ClearColor = clearColor,
            Background = rc.Background ?? BackgroundMode.SolidColor,
        };
        return new LoadedScene(ui, sources);
    }

    private static Vector3 ToVec3(float[] a) =>
        a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

    private static Quaternion ToQuat(float[] a) =>
        a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.Identity;
}
