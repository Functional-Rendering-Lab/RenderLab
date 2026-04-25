using System.Collections.Immutable;
using RenderLab.Functional;

namespace RenderLab.Graph;

/// <summary>
/// Logical identity for a render graph resource (e.g. "GBufferAlbedo", "DepthBuffer").
/// Two passes that reference the same <see cref="ResourceName"/> are linked by a data dependency.
/// Construct via <see cref="Create"/> (returns <see cref="Result{T,TError}"/>) or
/// <see cref="Of"/> (throws on invalid input — for compile-time literals only).
/// </summary>
public readonly record struct ResourceName
{
    public string Name { get; }

    private ResourceName(string name) { Name = name; }

    /// <summary>
    /// Smart constructor. Rejects null, empty, or whitespace names.
    /// </summary>
    public static Result<ResourceName, GraphError> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<ResourceName, GraphError>.Error(
                new GraphError.InvalidResourceName(name ?? "<null>"));
        return Result<ResourceName, GraphError>.Ok(new ResourceName(name));
    }

    /// <summary>
    /// Throwing convenience for literal call sites (a failure here is a programmer
    /// bug, not runtime input). Use <see cref="Create"/> when the input is dynamic.
    /// </summary>
    public static ResourceName Of(string name) =>
        Create(name).Match(
            ok: r => r,
            error: e => throw new ArgumentException($"Invalid ResourceName literal: {e}", nameof(name)));

    public override string ToString() => Name;
}

/// <summary>
/// Errors that can be produced by render-graph construction or compilation.
/// Used as the error channel for <see cref="ResourceName.Create"/>; future
/// compiler errors (cycles, duplicate writers) will land here too.
/// </summary>
public abstract record GraphError
{
    public sealed record InvalidResourceName(string Attempted) : GraphError
    {
        public override string ToString() => $"InvalidResourceName(\"{Attempted}\")";
    }
}

/// <summary>
/// How a render pass accesses a resource. Drives barrier insertion:
/// when consecutive passes use a resource with different usages, the compiler
/// inserts a <see cref="BarrierDesc"/> to transition between them.
/// </summary>
public enum ResourceUsage : byte
{
    /// <summary>Written as a color attachment (VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL).</summary>
    ColorAttachmentWrite,
    /// <summary>Written as a depth/stencil attachment (VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL).</summary>
    DepthStencilWrite,
    /// <summary>Read in a fragment/compute shader via sampler (VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL).</summary>
    ShaderRead,
    /// <summary>Presented to the swapchain (VK_IMAGE_LAYOUT_PRESENT_SRC_KHR).</summary>
    Present,
}

/// <summary>
/// Declares that a pass reads <paramref name="Resource"/> with the given <paramref name="Usage"/>.
/// Creates a data dependency on whichever pass writes that resource.
/// </summary>
public readonly record struct PassInput(ResourceName Resource, ResourceUsage Usage);

/// <summary>
/// Declares that a pass writes <paramref name="Resource"/> with the given <paramref name="Usage"/>.
/// Other passes that read this resource will be scheduled after this pass.
/// </summary>
public readonly record struct PassOutput(ResourceName Resource, ResourceUsage Usage);

/// <summary>
/// A render pass declaration: a named pass with its resource inputs and outputs.
/// This is the input to <see cref="RenderGraphCompiler.Compile"/>. Pure data — no execution logic.
/// The compiler uses the I/O declarations to determine execution order and barrier placement.
/// </summary>
public sealed record RenderPassDeclaration(
    string Name,
    ImmutableArray<PassInput> Inputs,
    ImmutableArray<PassOutput> Outputs);

/// <summary>
/// Describes a pipeline barrier that must be inserted before a pass executes.
/// Transitions <paramref name="Resource"/> from <paramref name="FromUsage"/> to <paramref name="ToUsage"/>,
/// which maps to a Vulkan image layout transition and appropriate stage/access masks.
/// </summary>
public readonly record struct BarrierDesc(
    ResourceName Resource,
    ResourceUsage FromUsage,
    ResourceUsage ToUsage);

/// <summary>
/// Output of <see cref="RenderGraphCompiler.Compile"/>: the original pass declaration
/// paired with any barriers that must be recorded before the pass begins.
/// Passes are returned in topologically sorted order — safe to execute sequentially.
/// </summary>
public sealed record ResolvedPass(
    RenderPassDeclaration Declaration,
    ImmutableArray<BarrierDesc> BarriersBefore);
