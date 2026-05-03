using System.Text.Json;

namespace RenderLab.Assets;

/// <summary>
/// Resolves symbolic asset references like
/// <c>{"kind":"procedural","generator":"sphere","params":{...}}</c> into the
/// CPU-side <see cref="MeshData"/> or <see cref="ProceduralTexture"/> the
/// shell uploads. Pure — sources are stateless generators, not loaders.
/// Implementations return <c>null</c> for generators they don't recognise so
/// callers can chain multiple sources.
/// </summary>
public interface IProceduralAssetSource
{
    MeshData? TryCreateMesh(string generator, IReadOnlyDictionary<string, JsonElement>? @params);
    ProceduralTexture? TryCreateTexture(string generator, IReadOnlyDictionary<string, JsonElement>? @params);
}

/// <summary>
/// CPU-side procedural texture data — no id, no GPU handle. The shell-side
/// registry assigns the <see cref="TextureId"/> when registering.
/// </summary>
public sealed record ProceduralTexture(int Width, int Height, TextureFormat Format, byte[] Pixels);
