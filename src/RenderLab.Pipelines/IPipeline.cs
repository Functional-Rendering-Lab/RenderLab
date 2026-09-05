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
/// (window, menu bar, panels). Pipelines that don't consume scenes (e.g.
/// triangle) ignore the scene argument.
/// <para>
/// Nothing here draws an interface. A pipeline that wanted a control used to draw
/// its own ImGui window, which put a UI framework in the dependencies of a
/// rendering technique and put the control somewhere the editor could not see it;
/// what a pipeline exposes now is state on <see cref="UiModel"/>, which the editor
/// already has panels for.
/// </para>
/// </summary>
public interface IPipeline : IDisposable
{
    /// <summary>Every visualization there is - what a pipeline offers unless it says otherwise.</summary>
    static readonly ImmutableArray<VisualizationMode> AllVisualizations =
        [.. Enum.GetValues<VisualizationMode>()];

    /// <summary>The pipeline's id as it appears in <c>project.json</c>.</summary>
    string Id { get; }

    /// <summary>True if the editor should expose the Scene + AssetBrowser
    /// panels and the pipeline is handed a built <c>Scene</c> in
    /// <see cref="RecordFrame"/>.</summary>
    bool ConsumesScenes { get; }

    /// <summary>
    /// Which visualizations this pipeline can actually resolve to the screen. The
    /// Visualization panel offers these and no others, and the Application moves
    /// <see cref="UiModel.Viz"/> into the set when a pipeline is loaded — so what
    /// the panel says and what the screen shows cannot disagree. A pipeline with
    /// no lighting has no Final and no HDR to offer, which is the case this
    /// exists for.
    /// </summary>
    ImmutableArray<VisualizationMode> SupportedVisualizations => AllVisualizations;

    /// <summary>
    /// Build long-lived GPU resources (render passes, pipeline state, descriptor
    /// layouts). Called once at engine startup after <see cref="GpuState"/> is
    /// ready.
    /// </summary>
    void Initialize(GpuState gpu, AssetRegistry assets);

    /// <summary>
    /// Recreate transient resources sized to <paramref name="target"/> - offscreen images,
    /// framebuffers, descriptor sets bound to those views - and hang the last pass's framebuffer
    /// on the target's own view.
    /// <para>
    /// Called on startup and whenever the target changes, which is a viewport resize rather than
    /// a window resize: dragging the boundary between the viewport and the panel beside it is a
    /// new size to render at, and dragging the window's edge is only one because the viewport
    /// inside it moved too. The device is idle when this is called.
    /// </para>
    /// </summary>
    void RecreateTransient(GpuState gpu, RenderTarget target);

    /// <summary>
    /// Read previous-frame GPU timestamps before <c>BeginFrame</c>. No-op for
    /// pipelines that don't time anything.
    /// </summary>
    void TickStats() { }

    /// <summary>
    /// Record one frame's draw commands into <paramref name="cb"/>. The command
    /// buffer is in the recording state. The Application handles
    /// BeginFrame/EndFrame and the editor's overlay pass after this returns.
    /// <para>
    /// What comes out is <paramref name="target"/>, left readable by a shader, because the editor
    /// draws it as a picture. A pipeline no longer touches the swapchain at all - it does not know
    /// which image is being presented, and cannot: the frame it renders is the size of a panel,
    /// not the size of the window.
    /// </para>
    /// <para>
    /// <paramref name="ui"/> is always supplied. It used to be null for pipelines
    /// that consume no scene, back when such a pipeline kept its own camera and
    /// its own visualization behind windows it drew itself; the editor owns both
    /// now, so every pipeline reads them from the same place.
    /// </para>
    /// </summary>
    void RecordFrame(GpuState gpu, CommandBuffer cb, Scene? scene, UiModel ui, double deltaSeconds, RenderTarget target);

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
