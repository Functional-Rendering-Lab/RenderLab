using System.Collections.Immutable;
using RenderLab.Functional;

namespace RenderLab.Graph;

/// <summary>
/// Pure, stateless compiler that transforms a set of render pass declarations into
/// an execution-ready sequence. Performs topological sorting (Kahn's algorithm) to
/// determine safe execution order, then inserts pipeline barriers where resource
/// usage changes between consecutive passes.
/// </summary>
public static class RenderGraphCompiler
{
    /// <summary>
    /// Compiles an unordered set of pass declarations into a topologically sorted sequence
    /// with barriers computed. This is a pure function — no GPU state, no side effects.
    /// Returns <see cref="GraphError"/> for malformed graphs (cycles, duplicate writers,
    /// inputs referencing unknown resources).
    /// </summary>
    /// <param name="passes">Unordered pass declarations. Each pass declares its resource I/O.</param>
    /// <returns>Either the resolved passes in execution order, or a <see cref="GraphError"/>.</returns>
    public static Result<ImmutableArray<ResolvedPass>, GraphError> Compile(
        ImmutableArray<RenderPassDeclaration> passes)
    {
        return BuildWriterMap(passes)
            .Bind(writerByResource => ValidateInputsHaveWriters(passes, writerByResource))
            .Bind(writerByResource => TopologicalSort(passes, writerByResource))
            .Map(InsertBarriers);
    }

    private static Result<Dictionary<ResourceName, string>, GraphError> BuildWriterMap(
        ImmutableArray<RenderPassDeclaration> passes)
    {
        var writerByResource = new Dictionary<ResourceName, string>();
        foreach (var pass in passes)
        {
            foreach (var output in pass.Outputs)
            {
                if (writerByResource.TryGetValue(output.Resource, out var existing))
                    return Result<Dictionary<ResourceName, string>, GraphError>.Error(
                        new GraphError.DuplicateWriter(output.Resource, existing, pass.Name));
                writerByResource[output.Resource] = pass.Name;
            }
        }
        return Result<Dictionary<ResourceName, string>, GraphError>.Ok(writerByResource);
    }

    private static Result<Dictionary<ResourceName, string>, GraphError> ValidateInputsHaveWriters(
        ImmutableArray<RenderPassDeclaration> passes,
        Dictionary<ResourceName, string> writerByResource)
    {
        foreach (var pass in passes)
        {
            foreach (var input in pass.Inputs)
            {
                if (!writerByResource.ContainsKey(input.Resource))
                    return Result<Dictionary<ResourceName, string>, GraphError>.Error(
                        new GraphError.UnknownResource(input.Resource, pass.Name));
            }
        }
        return Result<Dictionary<ResourceName, string>, GraphError>.Ok(writerByResource);
    }

    private static Result<ImmutableArray<RenderPassDeclaration>, GraphError> TopologicalSort(
        ImmutableArray<RenderPassDeclaration> passes,
        Dictionary<ResourceName, string> writerByResource)
    {
        var passByName = new Dictionary<string, RenderPassDeclaration>();
        var inDegree = new Dictionary<string, int>();
        var dependents = new Dictionary<string, List<string>>();

        foreach (var pass in passes)
        {
            passByName[pass.Name] = pass;
            inDegree.TryAdd(pass.Name, 0);
            dependents.TryAdd(pass.Name, []);
        }

        foreach (var pass in passes)
        {
            foreach (var input in pass.Inputs)
            {
                if (writerByResource.TryGetValue(input.Resource, out var writerName) &&
                    writerName != pass.Name)
                {
                    dependents[writerName].Add(pass.Name);
                    inDegree[pass.Name] = inDegree.GetValueOrDefault(pass.Name) + 1;
                }
            }
        }

        var queue = new Queue<string>();
        foreach (var (name, degree) in inDegree)
        {
            if (degree == 0) queue.Enqueue(name);
        }

        var sorted = ImmutableArray.CreateBuilder<RenderPassDeclaration>(passes.Length);

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            sorted.Add(passByName[name]);

            foreach (var dep in dependents[name])
            {
                inDegree[dep]--;
                if (inDegree[dep] == 0) queue.Enqueue(dep);
            }
        }

        if (sorted.Count != passes.Length)
        {
            var sortedNames = sorted.Select(p => p.Name).ToHashSet();
            var remaining = passes
                .Select(p => p.Name)
                .Where(n => !sortedNames.Contains(n))
                .ToImmutableArray();
            return Result<ImmutableArray<RenderPassDeclaration>, GraphError>.Error(
                new GraphError.Cycle(remaining));
        }

        return Result<ImmutableArray<RenderPassDeclaration>, GraphError>.Ok(sorted.ToImmutable());
    }

    private static ImmutableArray<ResolvedPass> InsertBarriers(
        ImmutableArray<RenderPassDeclaration> sortedPasses)
    {
        var lastUsage = new Dictionary<ResourceName, ResourceUsage>();
        var resolved = ImmutableArray.CreateBuilder<ResolvedPass>(sortedPasses.Length);

        foreach (var pass in sortedPasses)
        {
            var barriers = ImmutableArray.CreateBuilder<BarrierDesc>();

            foreach (var input in pass.Inputs)
            {
                if (lastUsage.TryGetValue(input.Resource, out var prevUsage) &&
                    prevUsage != input.Usage)
                {
                    barriers.Add(new BarrierDesc(input.Resource, prevUsage, input.Usage));
                }
            }

            foreach (var output in pass.Outputs)
            {
                if (lastUsage.TryGetValue(output.Resource, out var prevUsage) &&
                    prevUsage != output.Usage)
                {
                    barriers.Add(new BarrierDesc(output.Resource, prevUsage, output.Usage));
                }
            }

            resolved.Add(new ResolvedPass(pass, barriers.ToImmutable()));

            foreach (var input in pass.Inputs)
                lastUsage[input.Resource] = input.Usage;
            foreach (var output in pass.Outputs)
                lastUsage[output.Resource] = output.Usage;
        }

        return resolved.ToImmutable();
    }
}
