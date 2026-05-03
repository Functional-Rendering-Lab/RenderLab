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
/// registering its assets with the supplied <see cref="AssetRegistry"/> and
/// projecting its drawable seeds into a fresh <see cref="UiModel"/>. Index
/// references inside the document are mapped to typed registry ids in
/// dependency order: textures → materials (with rewritten albedo refs) →
/// meshes → drawables.
/// </summary>
public static class SceneLoader
{
    public static Result<UiModel, SceneLoadError> Load(
        string projectRoot,
        SceneDocument doc,
        AssetRegistry assets,
        IProceduralAssetSource procedural)
    {
        // Textures
        var textureIds = new TextureId[doc.Assets.Textures.Length];
        var fileTextureCache = new Dictionary<string, TextureId>(StringComparer.OrdinalIgnoreCase);
        var fileMeshCache = new Dictionary<string, MeshId>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < doc.Assets.Textures.Length; i++)
        {
            var entry = doc.Assets.Textures[i];
            switch (entry.Source)
            {
                case ProceduralSourceDoc p:
                {
                    var tex = procedural.TryCreateTexture(p.Generator, p.Params);
                    if (tex is null)
                        return Result.Error<UiModel, SceneLoadError>(
                            new SceneLoadError.UnknownProceduralGenerator("texture", p.Generator));
                    var r = assets.RegisterTexture(entry.Name, tex.Width, tex.Height, tex.Format, tex.Pixels);
                    if (r.IsError)
                        return Result.Error<UiModel, SceneLoadError>(
                            r.Match<SceneLoadError>(_ => null!, e => new SceneLoadError.AssetUploadFailed(entry.Name, e.Message)));
                    textureIds[i] = r.Match(ok: id => id, error: _ => default);
                    break;
                }
                case FileSourceDoc f:
                {
                    var maybe = ResolveFileTexture(projectRoot, f.Path, assets, fileTextureCache, fileMeshCache);
                    if (maybe.IsError) return Result.Error<UiModel, SceneLoadError>(maybe.Match<SceneLoadError>(_ => null!, e => e));
                    textureIds[i] = maybe.Match(ok: id => id, error: _ => default);
                    break;
                }
            }
        }

        // Materials (rewriting albedoMap index → TextureId)
        var materialIds = new MaterialId[doc.Assets.Materials.Length];
        for (int i = 0; i < doc.Assets.Materials.Length; i++)
        {
            switch (doc.Assets.Materials[i])
            {
                case BlinnPhongMaterialDoc bp:
                {
                    var albedoMap = bp.AlbedoMap is int amIdx ? textureIds[amIdx] : TextureId.None;
                    var r = assets.RegisterMaterial(bp.Name, id => new BlinnPhongMaterial(
                        id, bp.Name, ToVec3(bp.Albedo), bp.SpecularStrength, bp.Shininess, albedoMap));
                    if (r.IsError)
                        return Result.Error<UiModel, SceneLoadError>(
                            r.Match<SceneLoadError>(_ => null!, e => new SceneLoadError.AssetUploadFailed(bp.Name, e.Message)));
                    materialIds[i] = r.Match(ok: id => id, error: _ => default);
                    break;
                }
            }
        }

        // Meshes
        var meshIds = new MeshId[doc.Assets.Meshes.Length];
        for (int i = 0; i < doc.Assets.Meshes.Length; i++)
        {
            var entry = doc.Assets.Meshes[i];
            switch (entry.Source)
            {
                case ProceduralSourceDoc p:
                {
                    var data = procedural.TryCreateMesh(p.Generator, p.Params);
                    if (data is null)
                        return Result.Error<UiModel, SceneLoadError>(
                            new SceneLoadError.UnknownProceduralGenerator("mesh", p.Generator));
                    var r = assets.RegisterMesh(entry.Name, data);
                    if (r.IsError)
                        return Result.Error<UiModel, SceneLoadError>(
                            r.Match<SceneLoadError>(_ => null!, e => new SceneLoadError.AssetUploadFailed(entry.Name, e.Message)));
                    meshIds[i] = r.Match(ok: id => id, error: _ => default);
                    break;
                }
                case FileSourceDoc f:
                {
                    var maybe = ResolveFileMesh(projectRoot, f.Path, assets, fileTextureCache, fileMeshCache);
                    if (maybe.IsError) return Result.Error<UiModel, SceneLoadError>(maybe.Match<SceneLoadError>(_ => null!, e => e));
                    meshIds[i] = maybe.Match(ok: id => id, error: _ => default);
                    break;
                }
            }
        }

        // Drawables
        var drawables = ImmutableArray.CreateBuilder<EditableDrawable>(doc.Drawables.Length);
        foreach (var d in doc.Drawables)
        {
            drawables.Add(new EditableDrawable(
                LocalId: Guid.NewGuid(),
                Name: d.Name,
                Mesh: meshIds[d.Mesh],
                Transform: new Transform(ToVec3(d.Transform.Position), ToQuat(d.Transform.Rotation), d.Transform.Scale),
                Material: materialIds[d.Material]));
        }

        // Lights
        var lights = ImmutableArray.CreateBuilder<Light>(doc.Lights.Length);
        foreach (var l in doc.Lights)
        {
            lights.Add(l switch
            {
                PointLightDoc p => (Light)new PointLight(ToVec3(p.Position), ToVec3(p.Color), Intensity.Of(p.Intensity)),
                DirectionalLightDoc d2 => new DirectionalLight(
                    Direction.UnsafeFromUnit(Vector3.Normalize(ToVec3(d2.Direction))),
                    ToVec3(d2.Color),
                    Intensity.Of(d2.Intensity)),
                _ => throw new InvalidOperationException($"Unknown light DU case {l.GetType().Name}"),
            });
        }

        // Render config
        var shading = doc.RenderConfig.Shading.ToLowerInvariant() switch
        {
            "lambertian" => ShadingMode.Lambertian,
            "phong"      => ShadingMode.Phong,
            "blinnphong" => ShadingMode.BlinnPhong,
            _            => ShadingMode.BlinnPhong,
        };
        var viz = doc.RenderConfig.Viz.ToLowerInvariant() switch
        {
            "final"    => VisualizationMode.Final,
            "position" => VisualizationMode.Position,
            "normal"   => VisualizationMode.Normal,
            "albedo"   => VisualizationMode.Albedo,
            "depth"    => VisualizationMode.Depth,
            "hdr"      => VisualizationMode.HDR,
            _          => VisualizationMode.Final,
        };

        var camRad = MathF.PI / 180f;
        var camera = FreeCameraController.CreateDefault() with
        {
            Position = ToVec3(doc.Camera.Position),
            Yaw = doc.Camera.YawDeg * camRad,
            Pitch = doc.Camera.PitchDeg * camRad,
            FovRadians = doc.Camera.FovDeg * camRad,
        };

        var ui = UiModel.Default with
        {
            Camera = camera,
            Lights = lights.ToImmutable(),
            Ambient = new HemisphericAmbient(ToVec3(doc.Ambient.Sky), ToVec3(doc.Ambient.Ground)),
            Drawables = drawables.ToImmutable(),
            SelectedDrawable = drawables.Count > 0 ? drawables[0].LocalId : null,
            Shading = shading,
            LightingOnly = doc.RenderConfig.LightingOnly,
            Viz = viz,
            ClearColor = ToVec3(doc.RenderConfig.ClearColor),
        };
        return Result.Ok<UiModel, SceneLoadError>(ui);
    }

    // ─── File-source resolution (cached per-load) ─────────────────────

    private static Result<TextureId, SceneLoadError> ResolveFileTexture(
        string projectRoot, string path, AssetRegistry assets,
        Dictionary<string, TextureId> texCache, Dictionary<string, MeshId> meshCache)
    {
        var (filePath, _) = SplitSuffix(path);
        if (texCache.TryGetValue(filePath, out var existing))
            return Result.Ok<TextureId, SceneLoadError>(existing);

        var resolve = ProjectIO.ResolveProjectPath(projectRoot, filePath);
        if (resolve.IsError)
            return Result.Error<TextureId, SceneLoadError>(
                resolve.Match<SceneLoadError>(_ => null!, e => new SceneLoadError.FileSourceFailed(path, e.Message)));
        var absolute = resolve.Match(ok: p => p, error: _ => "");

        var import = assets.ImportGltf(absolute);
        if (import.IsError)
            return Result.Error<TextureId, SceneLoadError>(
                import.Match<SceneLoadError>(_ => null!, e => new SceneLoadError.FileSourceFailed(path, e.Message)));
        var result = import.Match(ok: r => r, error: _ => null!);
        if (result.Textures.Length == 0)
            return Result.Error<TextureId, SceneLoadError>(
                new SceneLoadError.FileSourceFailed(path, "imported file has no textures"));

        // Cache the first texture under the file path; cache meshes too so the
        // matching mesh source on the same file reuses the same import.
        var texId = result.Textures[0];
        texCache[filePath] = texId;
        if (result.Meshes.Length > 0) meshCache[filePath] = result.Meshes[0];
        return Result.Ok<TextureId, SceneLoadError>(texId);
    }

    private static Result<MeshId, SceneLoadError> ResolveFileMesh(
        string projectRoot, string path, AssetRegistry assets,
        Dictionary<string, TextureId> texCache, Dictionary<string, MeshId> meshCache)
    {
        var (filePath, _) = SplitSuffix(path);
        if (meshCache.TryGetValue(filePath, out var existing))
            return Result.Ok<MeshId, SceneLoadError>(existing);

        var resolve = ProjectIO.ResolveProjectPath(projectRoot, filePath);
        if (resolve.IsError)
            return Result.Error<MeshId, SceneLoadError>(
                resolve.Match<SceneLoadError>(_ => null!, e => new SceneLoadError.FileSourceFailed(path, e.Message)));
        var absolute = resolve.Match(ok: p => p, error: _ => "");

        var import = assets.ImportGltf(absolute);
        if (import.IsError)
            return Result.Error<MeshId, SceneLoadError>(
                import.Match<SceneLoadError>(_ => null!, e => new SceneLoadError.FileSourceFailed(path, e.Message)));
        var result = import.Match(ok: r => r, error: _ => null!);
        if (result.Meshes.Length == 0)
            return Result.Error<MeshId, SceneLoadError>(
                new SceneLoadError.FileSourceFailed(path, "imported file has no meshes"));

        var meshId = result.Meshes[0];
        meshCache[filePath] = meshId;
        if (result.Textures.Length > 0) texCache[filePath] = result.Textures[0];
        return Result.Ok<MeshId, SceneLoadError>(meshId);
    }

    private static (string Path, string? Suffix) SplitSuffix(string raw)
    {
        var hash = raw.IndexOf('#');
        return hash < 0 ? (raw, null) : (raw[..hash], raw[(hash + 1)..]);
    }

    // ─── Vector conversions ───────────────────────────────────────────

    private static Vector3 ToVec3(float[] a) =>
        a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

    private static Quaternion ToQuat(float[] a) =>
        a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.Identity;
}
