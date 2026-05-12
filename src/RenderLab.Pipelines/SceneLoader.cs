using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using RenderLab.Assets;
using RenderLab.Functional;
using RenderLab.Gpu.Assets;
using RenderLab.Project;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Pipelines;

/// <summary>
/// Bootstraps a runtime scene from a pure <see cref="SceneDocument"/> by
/// resolving every drawable's <see cref="AssetRef"/> against the project's
/// <see cref="AssetLibrary"/> and registering the resulting CPU-side data
/// with the <see cref="AssetRegistry"/>. Tracks the
/// <see cref="AssetRef"/> each registered runtime id came from in a
/// <see cref="SceneAssetSources"/> map, so the inverse
/// <c>SceneDocumentBuilder.From</c> can round-trip back to disk on save.
/// </summary>
public static class SceneLoader
{
    public sealed record LoadedScene(UiModel Ui, SceneAssetSources Sources);

    public static Result<LoadedScene, SceneLoadError> Load(
        string projectRoot,
        SceneDocument doc,
        AssetLibrary library,
        AssetRegistry assets,
        IProceduralAssetSource procedural)
    {
        var resolver = new Resolver(projectRoot, library, assets, procedural);

        var drawables = ImmutableArray.CreateBuilder<EditableDrawable>(doc.Drawables.Length);
        foreach (var d in doc.Drawables)
        {
            var meshR = resolver.ResolveMesh(d.Mesh);
            if (meshR.IsError) return Result.Error<LoadedScene, SceneLoadError>(meshR.Match<SceneLoadError>(_ => null!, e => e));
            var meshId = meshR.Match(ok: x => x, error: _ => default);

            var matR = resolver.ResolveMaterial(d.Material);
            if (matR.IsError) return Result.Error<LoadedScene, SceneLoadError>(matR.Match<SceneLoadError>(_ => null!, e => e));
            var matId = matR.Match(ok: x => x, error: _ => default);

            drawables.Add(new EditableDrawable(
                LocalId: Guid.NewGuid(),
                Name: d.Name,
                Mesh: meshId,
                Transform: new Transform(ToVec3(d.Transform.Position), ToQuat(d.Transform.Rotation), d.Transform.Scale),
                Material: matId));
        }

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
            Background = (doc.RenderConfig.Background ?? "solid").ToLowerInvariant() switch
            {
                "ambientgradient" => BackgroundMode.AmbientGradient,
                _                 => BackgroundMode.SolidColor,
            },
        };
        return Result.Ok<LoadedScene, SceneLoadError>(new LoadedScene(ui, resolver.Sources));
    }

    /// <summary>
    /// Per-load resolver. Caches registrations so the same <see cref="AssetRef"/>
    /// resolves to the same runtime id, and shares glTF imports across mesh +
    /// texture sub-asset references that share a file.
    /// </summary>
    private sealed class Resolver
    {
        private readonly string _projectRoot;
        private readonly AssetLibrary _library;
        private readonly AssetRegistry _assets;
        private readonly IProceduralAssetSource _procedural;

        private readonly Dictionary<AssetRef, MeshId> _meshCache = new();
        private readonly Dictionary<AssetRef, TextureId> _textureCache = new();
        private readonly Dictionary<AssetRef, MaterialId> _materialCache = new();
        private readonly Dictionary<string, GltfImportResult> _gltfCache = new(StringComparer.OrdinalIgnoreCase);

        public SceneAssetSources Sources { get; private set; } = SceneAssetSources.Empty;

        public Resolver(string projectRoot, AssetLibrary library, AssetRegistry assets, IProceduralAssetSource procedural)
        {
            _projectRoot = projectRoot;
            _library = library;
            _assets = assets;
            _procedural = procedural;
        }

        public Result<MeshId, SceneLoadError> ResolveMesh(AssetRef r)
        {
            if (_meshCache.TryGetValue(r, out var cached))
                return Result.Ok<MeshId, SceneLoadError>(cached);
            var entry = _library.Find(r.Guid);
            if (entry is null)
                return Result.Error<MeshId, SceneLoadError>(
                    new SceneLoadError.FileSourceFailed(r.ToString(), $"no library entry for guid {r.Guid:D}"));
            switch (entry)
            {
                case ProceduralAssetEntry p when p.Kind == AssetKind.Mesh:
                {
                    var data = _procedural.TryCreateMesh(p.Generator, ParamsToDict(p.Params));
                    if (data is null)
                        return Result.Error<MeshId, SceneLoadError>(
                            new SceneLoadError.UnknownProceduralGenerator("mesh", p.Generator));
                    var reg = _assets.RegisterMesh(p.Name, data);
                    if (reg.IsError)
                        return Result.Error<MeshId, SceneLoadError>(
                            new SceneLoadError.AssetUploadFailed(p.Name, reg.Match<AssetError>(_ => null!, e => e).Message));
                    var id = reg.Match(ok: x => x, error: _ => default);
                    _meshCache[r] = id;
                    Sources = Sources.WithMesh(id, r);
                    return Result.Ok<MeshId, SceneLoadError>(id);
                }
                case FileAssetEntry f when f.Kind == AssetKind.Mesh:
                {
                    var import = LoadGltfFor(f.ProjectRelativePath);
                    if (import.IsError) return Result.Error<MeshId, SceneLoadError>(import.Match<SceneLoadError>(_ => null!, e => e));
                    var ok = import.Match(ok: x => x, error: _ => null!);
                    var subIndex = ParseSubIndex(r.Sub, "mesh");
                    if (subIndex >= ok.Meshes.Length)
                        return Result.Error<MeshId, SceneLoadError>(
                            new SceneLoadError.FileSourceFailed(f.ProjectRelativePath, $"mesh #{subIndex} not in import"));
                    var id = ok.Meshes[subIndex];
                    _meshCache[r] = id;
                    Sources = Sources.WithMesh(id, r);
                    return Result.Ok<MeshId, SceneLoadError>(id);
                }
                default:
                    return Result.Error<MeshId, SceneLoadError>(
                        new SceneLoadError.FileSourceFailed(r.ToString(), $"entry is not a Mesh ({entry.Kind})"));
            }
        }

        public Result<TextureId, SceneLoadError> ResolveTexture(AssetRef r)
        {
            if (_textureCache.TryGetValue(r, out var cached))
                return Result.Ok<TextureId, SceneLoadError>(cached);
            var entry = _library.Find(r.Guid);
            if (entry is null)
                return Result.Error<TextureId, SceneLoadError>(
                    new SceneLoadError.FileSourceFailed(r.ToString(), $"no library entry for guid {r.Guid:D}"));
            switch (entry)
            {
                case ProceduralAssetEntry p when p.Kind == AssetKind.Texture:
                {
                    var tex = _procedural.TryCreateTexture(p.Generator, ParamsToDict(p.Params));
                    if (tex is null)
                        return Result.Error<TextureId, SceneLoadError>(
                            new SceneLoadError.UnknownProceduralGenerator("texture", p.Generator));
                    var reg = _assets.RegisterTexture(p.Name, tex.Width, tex.Height, tex.Format, tex.Pixels);
                    if (reg.IsError)
                        return Result.Error<TextureId, SceneLoadError>(
                            new SceneLoadError.AssetUploadFailed(p.Name, reg.Match<AssetError>(_ => null!, e => e).Message));
                    var id = reg.Match(ok: x => x, error: _ => default);
                    _textureCache[r] = id;
                    Sources = Sources.WithTexture(id, r);
                    return Result.Ok<TextureId, SceneLoadError>(id);
                }
                case FileAssetEntry f when f.Kind == AssetKind.Texture || f.Kind == AssetKind.Mesh:
                {
                    var import = LoadGltfFor(f.ProjectRelativePath);
                    if (import.IsError) return Result.Error<TextureId, SceneLoadError>(import.Match<SceneLoadError>(_ => null!, e => e));
                    var ok = import.Match(ok: x => x, error: _ => null!);
                    var subIndex = ParseSubIndex(r.Sub, "image");
                    if (subIndex >= ok.Textures.Length)
                        return Result.Error<TextureId, SceneLoadError>(
                            new SceneLoadError.FileSourceFailed(f.ProjectRelativePath, $"texture #{subIndex} not in import"));
                    var id = ok.Textures[subIndex];
                    _textureCache[r] = id;
                    Sources = Sources.WithTexture(id, r);
                    return Result.Ok<TextureId, SceneLoadError>(id);
                }
                default:
                    return Result.Error<TextureId, SceneLoadError>(
                        new SceneLoadError.FileSourceFailed(r.ToString(), $"entry is not a Texture ({entry.Kind})"));
            }
        }

        public Result<MaterialId, SceneLoadError> ResolveMaterial(AssetRef r)
        {
            if (_materialCache.TryGetValue(r, out var cached))
                return Result.Ok<MaterialId, SceneLoadError>(cached);
            var entry = _library.Find(r.Guid);
            if (entry is null)
                return Result.Error<MaterialId, SceneLoadError>(
                    new SceneLoadError.FileSourceFailed(r.ToString(), $"no library entry for guid {r.Guid:D}"));
            if (entry is not MaterialAssetEntry m)
                return Result.Error<MaterialId, SceneLoadError>(
                    new SceneLoadError.FileSourceFailed(r.ToString(), $"entry is not a Material ({entry.Kind})"));

            var albedoMap = TextureId.None;
            if (m.AlbedoTex is AssetRef tr)
            {
                var tres = ResolveTexture(tr);
                if (tres.IsError) return Result.Error<MaterialId, SceneLoadError>(tres.Match<SceneLoadError>(_ => null!, e => e));
                albedoMap = tres.Match(ok: x => x, error: _ => default);
            }

            var albedo = m.Params.Albedo;
            var reg = _assets.RegisterMaterial(m.Name, id => new BlinnPhongMaterial(
                id, m.Name,
                new Vector3(albedo[0], albedo[1], albedo[2]),
                m.Params.SpecularStrength,
                m.Params.Shininess,
                albedoMap));
            if (reg.IsError)
                return Result.Error<MaterialId, SceneLoadError>(
                    new SceneLoadError.AssetUploadFailed(m.Name, reg.Match<AssetError>(_ => null!, e => e).Message));
            var matId = reg.Match(ok: x => x, error: _ => default);
            _materialCache[r] = matId;
            Sources = Sources.WithMaterial(matId, r);
            return Result.Ok<MaterialId, SceneLoadError>(matId);
        }

        private Result<GltfImportResult, SceneLoadError> LoadGltfFor(string projectRelativePath)
        {
            if (_gltfCache.TryGetValue(projectRelativePath, out var cached))
                return Result.Ok<GltfImportResult, SceneLoadError>(cached);
            var resolved = ProjectIO.ResolveProjectPath(_projectRoot, projectRelativePath);
            if (resolved.IsError)
                return Result.Error<GltfImportResult, SceneLoadError>(
                    new SceneLoadError.FileSourceFailed(projectRelativePath, resolved.Match<ProjectError>(_ => null!, e => e).Message));
            var absolute = resolved.Match(ok: p => p, error: _ => "");
            var import = _assets.ImportGltf(absolute);
            if (import.IsError)
                return Result.Error<GltfImportResult, SceneLoadError>(
                    new SceneLoadError.FileSourceFailed(projectRelativePath, import.Match<AssetError>(_ => null!, e => e).Message));
            var ok = import.Match(ok: r => r, error: _ => null!);
            _gltfCache[projectRelativePath] = ok;
            return Result.Ok<GltfImportResult, SceneLoadError>(ok);
        }

        private static int ParseSubIndex(string? sub, string prefix)
        {
            if (string.IsNullOrEmpty(sub)) return 0;
            // Accept "mesh0", "mesh:0", or bare "0".
            var s = sub.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? sub[prefix.Length..].TrimStart(':')
                : sub;
            return int.TryParse(s, out var n) ? n : 0;
        }

        private static IReadOnlyDictionary<string, JsonElement>? ParamsToDict(JsonElement? p)
        {
            if (p is not JsonElement e || e.ValueKind != JsonValueKind.Object) return null;
            var dict = new Dictionary<string, JsonElement>();
            foreach (var prop in e.EnumerateObject())
                dict[prop.Name] = prop.Value.Clone();
            return dict;
        }
    }

    private static Vector3 ToVec3(float[] a) =>
        a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

    private static Quaternion ToQuat(float[] a) =>
        a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.Identity;
}
