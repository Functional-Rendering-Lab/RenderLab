using System.Numerics;
using System.Text.Json;
using RenderLab.Assets;
using RenderLab.Functional;
using RenderLab.Gpu.Assets;
using RenderLab.Project;

namespace RenderLab.Pipelines;

/// <summary>
/// Lazy bridge between project-level <see cref="AssetRef"/> (stable GUID
/// + sub-asset) and the runtime <see cref="AssetRegistry"/>'s typed ids
/// (<see cref="MeshId"/>, <see cref="TextureId"/>, <see cref="MaterialId"/>).
/// First <c>Resolve…</c> call for a given ref imports from source and
/// registers in the registry; subsequent calls return the cached id.
///
/// The resolver outlives a single scene: caches persist across scene
/// swaps so opening the same scene twice, or switching back and forth
/// between scenes that share assets, reuses the existing registrations
/// without re-uploading to the GPU. <see cref="Clear"/> wipes the cache
/// when the registry is wiped (project switch).
/// </summary>
public sealed class SceneAssetResolver
{
    private readonly AssetRegistry _assets;

    private string _projectRoot = "";
    private AssetLibrary _library = AssetLibrary.Empty;
    private IProceduralAssetSource _procedural = NullProcedural.Instance;

    private readonly Dictionary<AssetRef, MeshId> _meshCache = new();
    private readonly Dictionary<AssetRef, TextureId> _textureCache = new();
    private readonly Dictionary<AssetRef, MaterialId> _materialCache = new();
    private readonly Dictionary<string, GltfImportResult> _gltfCache = new(StringComparer.OrdinalIgnoreCase);

    public SceneAssetResolver(AssetRegistry assets) { _assets = assets; }

    /// <summary>
    /// Rebind the resolver to the current project context. Called after
    /// every <c>AssetLibraryScanner.Scan</c> so freshly added entries
    /// become resolvable. Does not invalidate existing cached
    /// registrations — id stability across rescans is the point.
    /// </summary>
    public void Bind(string projectRoot, AssetLibrary library, IProceduralAssetSource procedural)
    {
        _projectRoot = projectRoot;
        _library = library;
        _procedural = procedural;
    }

    /// <summary>
    /// Drop every cached resolution whose <see cref="AssetRef"/> matches
    /// the given guid. Used when the Asset Browser deletes an entry —
    /// the registered id (if any) is removed in lockstep by the shell,
    /// so the resolver's cache must not keep pointing at it.
    /// </summary>
    public void InvalidateGuid(Guid guid)
    {
        InvalidateDict(_meshCache, guid);
        InvalidateDict(_textureCache, guid);
        InvalidateDict(_materialCache, guid);
    }

    private static void InvalidateDict<TId>(Dictionary<AssetRef, TId> d, Guid g)
    {
        List<AssetRef>? matches = null;
        foreach (var k in d.Keys)
        {
            if (k.Guid == g)
            {
                matches ??= new();
                matches.Add(k);
            }
        }
        if (matches is null) return;
        foreach (var k in matches) d.Remove(k);
    }

    /// <summary>
    /// Drop every cached resolution. Used on project switch after the
    /// registry has been reset to built-ins — at that point the cached
    /// ids point at meshes/textures/materials that no longer exist.
    /// </summary>
    public void Clear()
    {
        _meshCache.Clear();
        _textureCache.Clear();
        _materialCache.Clear();
        _gltfCache.Clear();
    }

    /// <summary>
    /// Library entry behind a guid, or null if the current binding does
    /// not contain it. Lets callers introspect resolved references (e.g.
    /// to follow a material's albedo texture ref) without re-walking the
    /// project from scratch.
    /// </summary>
    public AssetEntry? LibraryEntry(Guid guid) => _library.Find(guid);

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

        var albedoMap = Optional<TextureId>.None;
        if (m.AlbedoTex.IsSome)
        {
            var tr = m.AlbedoTex.Match(some: x => x, none: () => default!);
            var tres = ResolveTexture(tr);
            if (tres.IsError) return Result.Error<MaterialId, SceneLoadError>(tres.Match<SceneLoadError>(_ => null!, e => e));
            albedoMap = Optional<TextureId>.Some(tres.Match(ok: x => x, error: _ => default));
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

    private sealed class NullProcedural : IProceduralAssetSource
    {
        public static readonly NullProcedural Instance = new();
        public MeshData? TryCreateMesh(string generator, IReadOnlyDictionary<string, JsonElement>? @params) => null;
        public ProceduralTexture? TryCreateTexture(string generator, IReadOnlyDictionary<string, JsonElement>? @params) => null;
    }
}
