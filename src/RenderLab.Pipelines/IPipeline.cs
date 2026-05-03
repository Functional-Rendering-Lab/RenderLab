using Silk.NET.Vulkan;
using RenderLab.Gpu;

namespace RenderLab.Pipelines;

using Scene = RenderLab.Scene.Scene;

/// <summary>
/// A rendering technique implementation — what was previously called a "demo."
/// Owns its render passes, descriptor set layouts, framebuffers, and per-frame
/// command recording. The <see cref="Application"/> hosts a single pipeline at
/// a time, hands it the active scene each frame, and supplies the editor UI
/// shell (window, ImGui, menu bar). Pipelines that don't consume scenes (e.g.
/// triangle) ignore the scene argument.
/// </summary>
public interface IPipeline : IDisposable
{
    /// <summary>The pipeline's id as it appears in <c>project.json</c>.</summary>
    string Id { get; }

    /// <summary>True if the editor should expose the Scene + AssetBrowser
    /// panels. False for pipelines that don't operate on a Scene snapshot.</summary>
    bool ConsumesScenes { get; }

    /// <summary>
    /// Build long-lived GPU resources (render passes, pipeline state, descriptor
    /// layouts, ImGui-compatible overlay framebuffer plumbing). Called once at
    /// engine startup after <see cref="GpuState"/> is ready.
    /// </summary>
    /// <param name="overlayRenderPass">The Application's pre-built overlay render
    /// pass (LoadOp.Load over the swapchain) — pipelines that draw debug viz
    /// before ImGui can use this.</param>
    void Initialize(GpuState gpu, RenderPass overlayRenderPass);

    /// <summary>
    /// Recreate transient resources sized to the swapchain (offscreen images,
    /// framebuffers, descriptor sets bound to those views). Called on startup
    /// and on every swapchain resize.
    /// </summary>
    void RecreateTransient(GpuState gpu);

    /// <summary>
    /// Record one frame's draw commands into <paramref name="cb"/>. The
    /// command buffer is already in the recording state. The Application
    /// handles BeginFrame/EndFrame around this call. <paramref name="scene"/>
    /// is null when <see cref="ConsumesScenes"/> is false.
    /// </summary>
    void RecordFrame(GpuState gpu, CommandBuffer cb, Scene? scene, double deltaSeconds, uint imageIndex);
}
