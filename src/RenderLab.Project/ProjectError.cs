namespace RenderLab.Project;

/// <summary>
/// Discriminated union of failures from <see cref="ProjectIO"/>. Pure data —
/// returned as the error track of <c>Result&lt;_, ProjectError&gt;</c>.
/// </summary>
public abstract record ProjectError(string Message)
{
    public sealed record FileNotFound(string Path) : ProjectError($"Not found: {Path}");
    public sealed record InvalidJson(string Path, string Reason) : ProjectError($"{Path}: {Reason}");
    public sealed record SchemaViolation(string Path, string Reason) : ProjectError($"{Path}: {Reason}");
    /// <summary>The project structure on disk is missing something required (e.g. <c>assets/</c> folder).</summary>
    public sealed record MissingProjectComponent(string Root, string What) : ProjectError($"{Root}: missing {What}");
    /// <summary>A scene-relative path tried to escape the project root after normalization.</summary>
    public sealed record PathEscape(string ProjectRoot, string Attempted) : ProjectError($"{Attempted} escapes project root {ProjectRoot}");
    /// <summary>An index reference in a scene document is out of range
    /// (e.g. drawable.mesh = 5 but only 3 meshes are declared).</summary>
    public sealed record DanglingIndex(string Where, int Value, int Max) : ProjectError($"{Where} index {Value} out of range (max {Max})");
}
