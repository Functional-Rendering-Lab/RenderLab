using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenderLab.Project;

/// <summary>
/// Builds an <see cref="AssetLibrary"/> from a <see cref="ProjectAssetIndex"/>.
/// Reads the <c>.meta</c> sidecar for every importable file and pairs it with
/// its source (path for files, JSON body for materials) into a typed entry.
/// </summary>
public static class AssetLibraryScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new OptionalJsonConverter() },
    };

    public static AssetLibrary Scan(ProjectAssetIndex index)
    {
        if (string.IsNullOrEmpty(index.ProjectRoot))
            return AssetLibrary.Empty;

        var builder = ImmutableDictionary.CreateBuilder<System.Guid, AssetEntry>();
        CollectFromFolder(index.Root, index.ProjectRoot, builder);
        return new AssetLibrary(builder.ToImmutable(), DateTime.UtcNow);
    }

    public static MaterialFileDoc? TryReadMaterial(string absolutePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(absolutePath);
            return JsonSerializer.Deserialize<MaterialFileDoc>(bytes, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void WriteMaterial(string absolutePath, MaterialFileDoc doc)
    {
        var dir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(doc, JsonOptions);
        File.WriteAllBytes(absolutePath, bytes);
    }

    private static void CollectFromFolder(
        ProjectFolderEntry folder,
        string projectRoot,
        ImmutableDictionary<System.Guid, AssetEntry>.Builder sink)
    {
        foreach (var sub in folder.Subfolders)
            CollectFromFolder(sub, projectRoot, sink);

        foreach (var file in folder.Files)
        {
            if (file.MetaGuid is not System.Guid guid) continue;
            if (sink.ContainsKey(guid)) continue;

            var displayName = Path.GetFileNameWithoutExtension(file.Name);
            // Strip the trailing ".mat" off .mat.json
            if (displayName.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                displayName = displayName[..^4];

            switch (file.Kind)
            {
                case ProjectFileKind.GltfModel:
                {
                    var meta = AssetMetaIO.TryRead(AssetMetaIO.MetaPathFor(file.AbsolutePath));
                    var import = meta?.Import ?? AssetMetaIO.DefaultImport(AssetKind.Mesh);
                    sink[guid] = new FileAssetEntry(
                        guid, AssetKind.Mesh, displayName, file.ProjectRelativePath, import);
                    break;
                }
                case ProjectFileKind.Image:
                {
                    var meta = AssetMetaIO.TryRead(AssetMetaIO.MetaPathFor(file.AbsolutePath));
                    var import = meta?.Import ?? AssetMetaIO.DefaultImport(AssetKind.Texture);
                    sink[guid] = new FileAssetEntry(
                        guid, AssetKind.Texture, displayName, file.ProjectRelativePath, import);
                    break;
                }
                case ProjectFileKind.Material:
                {
                    var doc = TryReadMaterial(file.AbsolutePath);
                    if (doc is null) continue;
                    sink[guid] = new MaterialAssetEntry(
                        guid, doc.Name, file.ProjectRelativePath, doc.Params, doc.AlbedoTex);
                    break;
                }
                case ProjectFileKind.Procedural:
                {
                    var doc = ProceduralAssetIO.TryRead(file.AbsolutePath);
                    if (doc is null) continue;
                    sink[guid] = new ProceduralAssetEntry(
                        guid, doc.Kind, doc.Name, file.ProjectRelativePath, doc.Generator, doc.Params);
                    break;
                }
            }
        }
    }
}
