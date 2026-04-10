using System.Collections.Immutable;

namespace RenderLab.Graph;

/// <summary>
/// Logical identity for a render graph resource (e.g. "GBufferAlbedo", "DepthBuffer").
/// Two passes that reference the same <see cref="ResourceName"/> are linked by a data dependency.
/// </summary>
public readonly record struct ResourceName(string Name)
{
    public override string ToString() => Name;
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
