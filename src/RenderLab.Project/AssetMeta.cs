using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenderLab.Project;

/// <summary>
/// On-disk sidecar for an importable asset file. Lives next to the source
/// file as <c>&lt;file&gt;.meta</c>. Holds the asset's stable GUID + typed
/// import settings — the file path is implicit (the sidecar's location).
/// </summary>
public sealed record AssetMetaDoc(
    System.Guid Guid,
    AssetKind Kind,
    ImportSettings? Import);

/// <summary>
/// Per-kind import settings. Kept polymorphic so each asset kind can grow
/// its own fields without forcing a giant flat record. <c>null</c> on
/// <see cref="AssetMetaDoc.Import"/> means "defaults for this kind".
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MeshImportSettings), "mesh")]
[JsonDerivedType(typeof(TextureImportSettings), "texture")]
[JsonDerivedType(typeof(MaterialImportSettings), "material")]
[JsonDerivedType(typeof(ProceduralImportSettings), "procedural")]
public abstract record ImportSettings;

public sealed record MeshImportSettings(float Scale = 1.0f) : ImportSettings;

public sealed record TextureImportSettings(bool SRgb = true, bool Mips = true) : ImportSettings;

public sealed record MaterialImportSettings() : ImportSettings;

public sealed record ProceduralImportSettings() : ImportSettings;

/// <summary>
/// Reads and writes <c>.meta</c> sidecars. Failures fall back to fresh
/// defaults rather than throw — a corrupt meta should not stop a scan.
/// </summary>
public static class AssetMetaIO
{
    public const string MetaExtension = ".meta";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Returns the meta sidecar path for an asset file (absolute or relative).</summary>
    public static string MetaPathFor(string assetPath) => assetPath + MetaExtension;

    public static AssetMetaDoc? TryRead(string metaAbsolutePath)
    {
        if (!File.Exists(metaAbsolutePath)) return null;
        try
        {
            var bytes = File.ReadAllBytes(metaAbsolutePath);
            return JsonSerializer.Deserialize<AssetMetaDoc>(bytes, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Write(string metaAbsolutePath, AssetMetaDoc doc)
    {
        var dir = Path.GetDirectoryName(metaAbsolutePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(doc, JsonOptions);
        File.WriteAllBytes(metaAbsolutePath, bytes);
    }

    /// <summary>
    /// Reads the sidecar next to <paramref name="assetAbsolutePath"/>, or
    /// creates one with a fresh <see cref="Guid"/> and default
    /// <see cref="ImportSettings"/> for <paramref name="kind"/>. Idempotent —
    /// subsequent calls return the same GUID.
    /// </summary>
    public static AssetMetaDoc ReadOrCreate(string assetAbsolutePath, AssetKind kind)
    {
        var metaPath = MetaPathFor(assetAbsolutePath);
        var existing = TryRead(metaPath);
        if (existing is not null) return existing;

        var doc = new AssetMetaDoc(System.Guid.NewGuid(), kind, DefaultImport(kind));
        Write(metaPath, doc);
        return doc;
    }

    public static ImportSettings DefaultImport(AssetKind kind) => kind switch
    {
        AssetKind.Mesh       => new MeshImportSettings(),
        AssetKind.Texture    => new TextureImportSettings(),
        AssetKind.Material   => new MaterialImportSettings(),
        AssetKind.Procedural => new ProceduralImportSettings(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
