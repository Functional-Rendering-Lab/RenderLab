namespace RenderLab.Pipelines;

public abstract record SceneLoadError(string Message)
{
    public sealed record UnknownProceduralGenerator(string Kind, string Generator)
        : SceneLoadError($"unknown procedural {Kind} generator '{Generator}'");
    public sealed record FileSourceFailed(string Path, string Reason)
        : SceneLoadError($"file source '{Path}' failed: {Reason}");
    public sealed record AssetUploadFailed(string What, string Reason)
        : SceneLoadError($"upload {What} failed: {Reason}");
    public sealed record InvalidEnum(string Field, string Value)
        : SceneLoadError($"unknown {Field} '{Value}'");
    public sealed record InvalidLightKind(string TypeName)
        : SceneLoadError($"unknown light DU case '{TypeName}'");
    public sealed record InvalidValue(string Field, string Reason)
        : SceneLoadError($"invalid {Field}: {Reason}");
}
