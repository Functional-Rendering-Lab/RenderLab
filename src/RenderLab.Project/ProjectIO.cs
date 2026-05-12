using System.Text.Json;
using System.Text.Json.Serialization;
using RenderLab.Functional;

namespace RenderLab.Project;

/// <summary>
/// Reads and writes the on-disk project format. The data model lives in
/// <see cref="ProjectManifest"/> + <see cref="SceneDocument"/>; this class is
/// the JSON + filesystem boundary. Pure-ish: file I/O is the only side
/// effect, and every operation returns a <see cref="Result{T, TError}"/> so
/// callers don't have to throw to handle malformed input.
/// </summary>
public static class ProjectIO
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Manifest filename at the root of every project folder.</summary>
    public const string ManifestFileName = "project.json";

    /// <summary>Mandatory subfolder for vendored binary assets.</summary>
    public const string AssetsFolderName = "assets";

    public static Result<ProjectManifest, ProjectError> ReadManifest(string projectRoot)
    {
        var manifestPath = Path.Combine(projectRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
            return Result.Error<ProjectManifest, ProjectError>(new ProjectError.FileNotFound(manifestPath));

        var assetsDir = Path.Combine(projectRoot, AssetsFolderName);
        if (!Directory.Exists(assetsDir))
            return Result.Error<ProjectManifest, ProjectError>(
                new ProjectError.MissingProjectComponent(projectRoot, $"{AssetsFolderName}/ folder"));

        try
        {
            var bytes = File.ReadAllBytes(manifestPath);
            var manifest = JsonSerializer.Deserialize<ProjectManifest>(bytes, JsonOptions)
                ?? throw new InvalidDataException("manifest deserialised to null");
            return Result.Ok<ProjectManifest, ProjectError>(manifest);
        }
        catch (JsonException ex)
        {
            return Result.Error<ProjectManifest, ProjectError>(
                new ProjectError.InvalidJson(manifestPath, ex.Message));
        }
        catch (Exception ex)
        {
            return Result.Error<ProjectManifest, ProjectError>(
                new ProjectError.SchemaViolation(manifestPath, ex.Message));
        }
    }

    public static void WriteManifest(string projectRoot, ProjectManifest manifest)
    {
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, AssetsFolderName));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        File.WriteAllBytes(Path.Combine(projectRoot, ManifestFileName), bytes);
    }

    /// <summary>
    /// Reads a scene from disk in the new AssetRef shape. Legacy index-based
    /// files are detected, run through <see cref="SceneUpgrader"/>, and the
    /// upgraded document is written back to <paramref name="sceneRelative"/>
    /// so subsequent reads skip the conversion.
    /// </summary>
    public static Result<SceneDocument, ProjectError> ReadScene(string projectRoot, string sceneRelative)
    {
        var resolved = ResolveProjectPath(projectRoot, sceneRelative);
        return resolved.Match(
            ok: absolute =>
            {
                if (!File.Exists(absolute))
                    return Result.Error<SceneDocument, ProjectError>(new ProjectError.FileNotFound(absolute));
                try
                {
                    var bytes = File.ReadAllBytes(absolute);
                    if (IsLegacy(bytes))
                    {
                        var legacy = JsonSerializer.Deserialize<LegacySceneDocument>(bytes, JsonOptions)
                            ?? throw new InvalidDataException("scene deserialised to null");
                        var validate = ValidateLegacyIndices(legacy, absolute);
                        if (validate.IsError) return validate;
                        var upgraded = SceneUpgrader.Upgrade(legacy, projectRoot);
                        return upgraded.Match(
                            ok: doc =>
                            {
                                // Persist the upgraded form so the conversion
                                // is a one-shot — next read skips this branch.
                                WriteSceneRaw(absolute, doc);
                                return Result.Ok<SceneDocument, ProjectError>(doc);
                            },
                            error: e => Result.Error<SceneDocument, ProjectError>(e));
                    }
                    var doc2 = JsonSerializer.Deserialize<SceneDocument>(bytes, JsonOptions)
                        ?? throw new InvalidDataException("scene deserialised to null");
                    return Result.Ok<SceneDocument, ProjectError>(doc2);
                }
                catch (JsonException ex)
                {
                    return Result.Error<SceneDocument, ProjectError>(new ProjectError.InvalidJson(absolute, ex.Message));
                }
                catch (Exception ex)
                {
                    return Result.Error<SceneDocument, ProjectError>(new ProjectError.SchemaViolation(absolute, ex.Message));
                }
            },
            error: err => Result.Error<SceneDocument, ProjectError>(err));
    }

    public static Result<Unit, ProjectError> WriteScene(string projectRoot, string sceneRelative, SceneDocument scene)
    {
        var resolved = ResolveProjectPath(projectRoot, sceneRelative);
        return resolved.Match(
            ok: absolute =>
            {
                WriteSceneRaw(absolute, scene);
                return Result.Ok<Unit, ProjectError>(Unit.Value);
            },
            error: err => Result.Error<Unit, ProjectError>(err));
    }

    private static void WriteSceneRaw(string absolute, SceneDocument scene)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(scene, JsonOptions);
        File.WriteAllBytes(absolute, bytes);
    }

    /// <summary>
    /// Combines a project-relative path with the project root, normalises the
    /// result, and rejects anything that escapes <paramref name="projectRoot"/>
    /// (e.g. <c>../../etc/passwd</c>). Returns an absolute, canonical path on
    /// success.
    /// </summary>
    public static Result<string, ProjectError> ResolveProjectPath(string projectRoot, string projectRelative)
    {
        var rootFull = Path.GetFullPath(projectRoot);
        var combined = Path.GetFullPath(Path.Combine(rootFull, projectRelative));
        var rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        if (!combined.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
            && !combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Error<string, ProjectError>(
                new ProjectError.PathEscape(rootFull, projectRelative));
        }
        return Result.Ok<string, ProjectError>(combined);
    }

    private static bool IsLegacy(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("assets", out _);
        }
        catch
        {
            return false;
        }
    }

    private static Result<SceneDocument, ProjectError> ValidateLegacyIndices(LegacySceneDocument doc, string path)
    {
        var meshCount = doc.Assets.Meshes.Length;
        var matCount = doc.Assets.Materials.Length;
        var texCount = doc.Assets.Textures.Length;

        for (int i = 0; i < doc.Assets.Materials.Length; i++)
        {
            if (doc.Assets.Materials[i] is LegacyBlinnPhongMaterialDoc bp && bp.AlbedoMap is int amIdx)
            {
                if (amIdx < 0 || amIdx >= texCount)
                    return Result.Error<SceneDocument, ProjectError>(
                        new ProjectError.DanglingIndex($"{path} materials[{i}].albedoMap", amIdx, texCount - 1));
            }
        }
        for (int i = 0; i < doc.Drawables.Length; i++)
        {
            var d = doc.Drawables[i];
            if (d.Mesh < 0 || d.Mesh >= meshCount)
                return Result.Error<SceneDocument, ProjectError>(
                    new ProjectError.DanglingIndex($"{path} drawables[{i}].mesh", d.Mesh, meshCount - 1));
            if (d.Material < 0 || d.Material >= matCount)
                return Result.Error<SceneDocument, ProjectError>(
                    new ProjectError.DanglingIndex($"{path} drawables[{i}].material", d.Material, matCount - 1));
        }
        return Result.Ok<SceneDocument, ProjectError>(null!);
    }
}

/// <summary>Empty value for <c>Result&lt;Unit, _&gt;</c> when the success
/// case carries no data.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
}
