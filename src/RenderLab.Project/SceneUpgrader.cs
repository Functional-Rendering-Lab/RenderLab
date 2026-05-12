using System.Text.Json;
using RenderLab.Functional;

namespace RenderLab.Project;

/// <summary>
/// Migrates legacy index-based scene documents to the AssetRef-based shape.
/// Impure: writes <c>.proc.meta</c> procedural-asset files, <c>.mat.json</c>
/// material files (and their <c>.meta</c> sidecars), and ensures
/// <c>.meta</c> sidecars exist for every referenced file source.
/// </summary>
public static class SceneUpgrader
{
    public static Result<SceneDocument, ProjectError> Upgrade(
        LegacySceneDocument legacy,
        string projectRoot)
    {
        // Resolve each legacy mesh / texture entry to a stable AssetRef.
        var meshRefs = new AssetRef[legacy.Assets.Meshes.Length];
        for (int i = 0; i < legacy.Assets.Meshes.Length; i++)
        {
            var e = legacy.Assets.Meshes[i];
            var r = ResolveSource(projectRoot, AssetKind.Mesh, e.Name, e.Source, isMesh: true);
            if (r.IsError) return Result.Error<SceneDocument, ProjectError>(r.Match<ProjectError>(_ => null!, x => x));
            meshRefs[i] = r.Match(ok: x => x, error: _ => default);
        }

        var textureRefs = new AssetRef[legacy.Assets.Textures.Length];
        for (int i = 0; i < legacy.Assets.Textures.Length; i++)
        {
            var e = legacy.Assets.Textures[i];
            var r = ResolveSource(projectRoot, AssetKind.Texture, e.Name, e.Source, isMesh: false);
            if (r.IsError) return Result.Error<SceneDocument, ProjectError>(r.Match<ProjectError>(_ => null!, x => x));
            textureRefs[i] = r.Match(ok: x => x, error: _ => default);
        }

        // Materials → .mat.json files under assets/materials/, with sidecars.
        var matRefs = new AssetRef[legacy.Assets.Materials.Length];
        for (int i = 0; i < legacy.Assets.Materials.Length; i++)
        {
            var m = legacy.Assets.Materials[i];
            var r = WriteMaterialFile(projectRoot, m, textureRefs);
            if (r.IsError) return Result.Error<SceneDocument, ProjectError>(r.Match<ProjectError>(_ => null!, x => x));
            matRefs[i] = r.Match(ok: x => x, error: _ => default);
        }

        var drawables = new DrawableDoc[legacy.Drawables.Length];
        for (int i = 0; i < legacy.Drawables.Length; i++)
        {
            var d = legacy.Drawables[i];
            drawables[i] = new DrawableDoc(
                d.Name,
                meshRefs[d.Mesh],
                matRefs[d.Material],
                d.Transform);
        }

        return Result.Ok<SceneDocument, ProjectError>(new SceneDocument(
            Version: 2,
            Camera: legacy.Camera,
            Ambient: legacy.Ambient,
            Lights: legacy.Lights,
            RenderConfig: legacy.RenderConfig,
            Drawables: drawables));
    }

    private static Result<AssetRef, ProjectError> ResolveSource(
        string projectRoot, AssetKind kind, string displayName, LegacyAssetSourceDoc src, bool isMesh)
    {
        switch (src)
        {
            case LegacyFileSourceDoc f:
            {
                var (path, suffix) = SplitSuffix(f.Path);
                var resolved = ProjectIO.ResolveProjectPath(projectRoot, path);
                if (resolved.IsError)
                    return Result.Error<AssetRef, ProjectError>(resolved.Match<ProjectError>(_ => null!, e => e));
                var abs = resolved.Match(ok: p => p, error: _ => "");
                // The referenced file may not exist yet (asset will fail to
                // load); still create a stable .meta so future scans converge.
                AssetMetaDoc meta;
                try
                {
                    meta = AssetMetaIO.ReadOrCreate(abs, kind);
                }
                catch (Exception ex)
                {
                    return Result.Error<AssetRef, ProjectError>(new ProjectError.SchemaViolation(abs, ex.Message));
                }
                return Result.Ok<AssetRef, ProjectError>(new AssetRef(meta.Guid, suffix));
            }
            case LegacyProceduralSourceDoc p:
            {
                var procDir = Path.Combine(projectRoot, "assets", "proc");
                Directory.CreateDirectory(procDir);
                var fileName = UniqueName(procDir, $"{Sanitize(p.Generator)}_{Sanitize(displayName)}", ProceduralAssetIO.ProcExtension);
                var abs = Path.Combine(procDir, fileName);
                JsonElement? paramsElem = null;
                if (p.Params is { Count: > 0 })
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(p.Params);
                    paramsElem = JsonDocument.Parse(bytes).RootElement.Clone();
                }
                var guid = System.Guid.NewGuid();
                ProceduralAssetIO.Write(abs, new ProceduralFileDoc(
                    Version: 1,
                    Guid: guid,
                    Kind: kind,
                    Name: displayName,
                    Generator: p.Generator,
                    Params: paramsElem));
                return Result.Ok<AssetRef, ProjectError>(new AssetRef(guid));
            }
            default:
                return Result.Error<AssetRef, ProjectError>(
                    new ProjectError.SchemaViolation("(scene)", $"unknown legacy source {src.GetType().Name}"));
        }
    }

    private static Result<AssetRef, ProjectError> WriteMaterialFile(
        string projectRoot, LegacyMaterialDoc m, AssetRef[] textureRefs)
    {
        if (m is not LegacyBlinnPhongMaterialDoc bp)
            return Result.Error<AssetRef, ProjectError>(
                new ProjectError.SchemaViolation("(scene)", $"unsupported material kind {m.GetType().Name}"));

        var matDir = Path.Combine(projectRoot, "assets", "materials");
        Directory.CreateDirectory(matDir);
        var fileName = UniqueName(matDir, Sanitize(bp.Name), ".mat.json");
        var abs = Path.Combine(matDir, fileName);

        AssetRef? albedoTex = null;
        if (bp.AlbedoMap is int idx && idx >= 0 && idx < textureRefs.Length)
            albedoTex = textureRefs[idx];

        AssetLibraryScanner.WriteMaterial(abs, new MaterialFileDoc(
            Version: 1,
            Name: bp.Name,
            Params: new MaterialParamsDoc(bp.Albedo, bp.SpecularStrength, bp.Shininess),
            AlbedoTex: albedoTex));
        var meta = AssetMetaIO.ReadOrCreate(abs, AssetKind.Material);
        return Result.Ok<AssetRef, ProjectError>(new AssetRef(meta.Guid));
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = s.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray();
        var cleaned = new string(chars);
        return string.IsNullOrEmpty(cleaned) ? "asset" : cleaned;
    }

    private static string UniqueName(string dir, string baseName, string extension)
    {
        var candidate = baseName + extension;
        if (!File.Exists(Path.Combine(dir, candidate))) return candidate;
        for (int i = 2; i < 10_000; i++)
        {
            candidate = $"{baseName}_{i}{extension}";
            if (!File.Exists(Path.Combine(dir, candidate))) return candidate;
        }
        return $"{baseName}_{System.Guid.NewGuid():N}{extension}";
    }

    private static (string Path, string? Suffix) SplitSuffix(string raw)
    {
        var hash = raw.IndexOf('#');
        return hash < 0 ? (raw, null) : (raw[..hash], raw[(hash + 1)..]);
    }
}
