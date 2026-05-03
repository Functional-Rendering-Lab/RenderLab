namespace RenderLab.Pipelines;

/// <summary>Failures from <see cref="PipelineRegistry"/>.</summary>
public abstract record PipelineError(string Message)
{
    public sealed record UnknownPipelineId(string Id, IReadOnlyCollection<string> Available)
        : PipelineError($"Unknown pipeline '{Id}'. Available: {string.Join(", ", Available)}");
}
