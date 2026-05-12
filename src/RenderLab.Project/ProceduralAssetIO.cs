using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenderLab.Project;

/// <summary>
/// Reads and writes <c>*.proc.meta</c> files — self-describing procedural
/// asset definitions. Unlike file-backed assets, procedurals carry their
/// identity (guid) and generator metadata in the same file; there is no
/// sibling sidecar.
/// </summary>
public static class ProceduralAssetIO
{
    public const string ProcExtension = ".proc.meta";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ProceduralFileDoc? TryRead(string absolutePath)
    {
        if (!File.Exists(absolutePath)) return null;
        try
        {
            var bytes = File.ReadAllBytes(absolutePath);
            return JsonSerializer.Deserialize<ProceduralFileDoc>(bytes, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Write(string absolutePath, ProceduralFileDoc doc)
    {
        var dir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(doc, JsonOptions);
        File.WriteAllBytes(absolutePath, bytes);
    }
}
