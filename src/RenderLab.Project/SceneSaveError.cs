using RenderLab.Assets;

namespace RenderLab.Project;

/// <summary>
/// Failures returned by <see cref="SceneDocumentBuilder"/>. Pure data;
/// the caller decides how to surface them (console log, modal dialog).
/// </summary>
public abstract record SceneSaveError(string Message)
{
    public sealed record MissingMeshSource(MeshId Id, string Name)
        : SceneSaveError($"mesh '{Name}' (id #{Id.Value}) has no recorded source — was it imported via a path the loader didn't track?");
    public sealed record MissingTextureSource(TextureId Id, string Name)
        : SceneSaveError($"texture '{Name}' (id #{Id.Value}) has no recorded source");
    public sealed record UnknownAsset(string Kind, int Id)
        : SceneSaveError($"unknown {Kind} id #{Id} (catalog lost the registration?)");
    public sealed record UnsupportedMaterialKind(string TypeName)
        : SceneSaveError($"material kind '{TypeName}' has no document representation yet");
    public sealed record WriteFailed(string Path, string Reason)
        : SceneSaveError($"writing scene '{Path}' failed: {Reason}");
}
