using System.Collections.Immutable;

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
    /// </summary>
    /// <param name="passes">Unordered pass declarations. Each pass declares its resource I/O.</param>
    /// <returns>Passes in execution order, each annotated with barriers to insert before it runs.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the pass graph contains a cycle.</exception>
    public static ImmutableArray<ResolvedPass> Compile(ImmutableArray<RenderPassDeclaration> passes)
    {
        var sorted = TopologicalSort(passes);
        return InsertBarriers(sorted);
    }

    private static ImmutableArray<RenderPassDeclaration> TopologicalSort(
        ImmutableArray<RenderPassDeclaration> passes)
    {
        // Build: which resources does each pass write?
        var writerByResource = new Dictionary<ResourceName, string>();
        foreach (var pass in passes)
        {
            foreach (var output in pass.Outputs)
                writerByResource[output.Resource] = pass.Name;
        }

        // Build adjacency: pass depends on writer of each input resource
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

        // Kahn's algorithm
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
            throw new InvalidOperationException(
                "Render graph contains a cycle — passes cannot be ordered.");

        return sorted.ToImmutable();
    }

    private static ImmutableArray<ResolvedPass> InsertBarriers(
        ImmutableArray<RenderPassDeclaration> sortedPasses)
    {
        // Track last known usage of each resource
        var lastUsage = new Dictionary<ResourceName, ResourceUsage>();
        var resolved = ImmutableArray.CreateBuilder<ResolvedPass>(sortedPasses.Length);

        foreach (var pass in sortedPasses)
        {
            var barriers = ImmutableArray.CreateBuilder<BarrierDesc>();

            // Check inputs — if resource was last used differently, insert barrier
            foreach (var input in pass.Inputs)
            {
                if (lastUsage.TryGetValue(input.Resource, out var prevUsage) &&
                    prevUsage != input.Usage)
                {
                    barriers.Add(new BarrierDesc(input.Resource, prevUsage, input.Usage));
                }
            }

            // Check outputs — if resource was last used differently, insert barrier
            foreach (var output in pass.Outputs)
            {
                if (lastUsage.TryGetValue(output.Resource, out var prevUsage) &&
                    prevUsage != output.Usage)
                {
                    barriers.Add(new BarrierDesc(output.Resource, prevUsage, output.Usage));
                }
            }

            resolved.Add(new ResolvedPass(pass, barriers.ToImmutable()));

            // Update last usage
            foreach (var input in pass.Inputs)
                lastUsage[input.Resource] = input.Usage;
            foreach (var output in pass.Outputs)
                lastUsage[output.Resource] = output.Usage;
        }

        return resolved.ToImmutable();
    }
}
