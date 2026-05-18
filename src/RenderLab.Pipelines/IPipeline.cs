using System.Collections.Immutable;
using Silk.NET.Vulkan;
using RenderLab.Gpu;
using RenderLab.Gpu.Assets;
using RenderLab.Graph;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Pipelines;

using Scene = RenderLab.Scene.Scene;

/// <summary>
/// A rendering technique implementation — what was previously called a "demo."
/// Owns its render passes, descriptor set layouts, framebuffers, and per-frame
/// command recording. The <c>Application</c> hosts a single pipeline at a time,
/// hands it the active scene each frame, and supplies the editor UI shell
/// (window, ImGui, menu bar). Pipelines that don't consume scenes (e.g.
/// triangle) ignore the scene argument.
/// </summary>
public interface IPipeline : IDisposable
{
    /// <summary>The pipeline's id as it appears in <c>project.json</c>.</summary>
    string Id { get; }

    /// <summary>True if the editor should expose the Scene + AssetBrowser
    /// panels and the pipeline reads <see cref="UiModel"/> in
    /// <see cref="RecordFrame"/>.</summary>
    bool ConsumesScenes { get; }

    /// <summary>
    /// Build long-lived GPU resources (render passes, pipeline state, descriptor
    /// layouts). Called once at engine startup after <see cref="GpuState"/> is
    /// ready.
    /// </summary>
    /// <param name="overlayRenderPass">The Application's pre-built overlay
    /// render pass (LoadOp.Load over the swapchain) for ImGui.</param>
    void Initialize(GpuState gpu, AssetRegistry assets, RenderPass overlayRenderPass);

    /// <summary>
    /// Recreate transient resources sized to the swapchain (offscreen images,
    /// framebuffers, descriptor sets bound to those views). Called on startup
    /// and on every swapchain resize.
    /// </summary>
    void RecreateTransient(GpuState gpu);

    /// <summary>
    /// Read previous-frame GPU timestamps before <c>BeginFrame</c>. No-op for
    /// pipelines that don't time anything.
    /// </summary>
    void TickStats() { }

    /// <summary>
    /// Forward a per-frame camera input to pipelines that own their own
    /// camera state. <c>ConsumesScenes</c> pipelines ignore this — the
    /// Application updates <see cref="UiModel.Camera"/> directly. The
    /// Application skips this call when the previous frame's UI captured the
    /// mouse.
    /// </summary>
    void HandleInput(CameraInput input) { }

    /// <summary>
    /// Record one frame's draw commands into <paramref name="cb"/>. The command
    /// buffer is in the recording state. The Application handles
    /// BeginFrame/EndFrame and the ImGui overlay pass after this returns;
    /// pipelines must leave the swapchain image in <c>PresentSrcKhr</c>.
    /// </summary>
    void RecordFrame(GpuState gpu, CommandBuffer cb, Scene? scene, UiModel? ui, double deltaSeconds, uint imageIndex);

    /// <summary>
    /// Draw any pipeline-specific ImGui windows (e.g. GBuffer's vizMode
    /// combo). Called between <c>NewFrame</c> and the editor panels. The
    /// editor's <c>UiView</c> already covers the deferred panels — this is
    /// for pipelines that ride outside that pure shell.
    /// </summary>
    void DrawDebugUi() { }

    /// <summary>
    /// Per-frame snapshot of pipeline-internal stats (GPU timestamps, the
    /// resolved render graph) for the editor's debug panels. Pipelines that
    /// don't time anything return an empty value.
    /// </summary>
    FrameStats GetFrameStats(double deltaSeconds) =>
        new((float)deltaSeconds, Array.Empty<string>(), Array.Empty<double>(), ImmutableArray<ResolvedPass>.Empty);

    /// <summary>
    /// Drop any caches keyed on registered asset ids. Reserved hook for
    /// callers that need to force-invalidate pipeline-internal id-keyed
    /// state; the Application no longer invokes it on scene swap now
    /// that the registry persists ids across scenes. Default no-op.
    /// </summary>
    void ResetSceneState() { }

    /// <summary>
    /// Rebuild VkPipelines from freshly-compiled SPIR-V on disk. Engine has
    /// already <c>vkDeviceWaitIdle</c>'d; the pipeline destroys its previous
    /// <c>VkPipeline</c>/<c>PipelineLayout</c> handles and recreates them
    /// from the same <c>shaders/</c> directory used at startup. Render
    /// passes, descriptor layouts, and transient images are preserved.
    /// Default no-op for pipelines that opt out of hot reload.
    /// </summary>
    void ReloadShaders(GpuState gpu) { }
}
