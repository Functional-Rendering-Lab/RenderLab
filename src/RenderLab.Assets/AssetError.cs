namespace RenderLab.Assets;

/// <summary>
/// Discriminated union of failures that can occur when loading or registering
/// an asset. Pure data — used as the error track of <c>Result&lt;_, AssetError&gt;</c>
/// returned by registry operations.
/// </summary>
public abstract record AssetError(string Message)
{
    public sealed record FileNotFound(string Path) : AssetError($"Not found: {Path}");
    public sealed record InvalidFormat(string Path, string Reason) : AssetError($"{Path}: {Reason}");
    public sealed record GpuUploadFailed(string Reason) : AssetError(Reason);
    public sealed record UnknownId(string Kind, int Value) : AssetError($"Unknown {Kind}({Value})");
}
