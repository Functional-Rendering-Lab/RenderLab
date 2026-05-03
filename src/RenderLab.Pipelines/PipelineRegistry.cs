using RenderLab.Functional;

namespace RenderLab.Pipelines;

/// <summary>
/// Maps <c>project.json</c> pipeline-id strings to factory functions. Populated
/// once at startup (a few lines in <c>Program.cs</c>); resolved per project.
/// New pipelines register here.
/// </summary>
public sealed class PipelineRegistry
{
    private readonly Dictionary<string, Func<IPipeline>> _factories = new(StringComparer.OrdinalIgnoreCase);

    public PipelineRegistry Register(string id, Func<IPipeline> factory)
    {
        _factories[id] = factory;
        return this;
    }

    public Result<IPipeline, PipelineError> Resolve(string id) =>
        _factories.TryGetValue(id, out var factory)
            ? Result.Ok<IPipeline, PipelineError>(factory())
            : Result.Error<IPipeline, PipelineError>(
                new PipelineError.UnknownPipelineId(id, _factories.Keys.ToArray()));

    public IReadOnlyCollection<string> RegisteredIds => _factories.Keys;
}
