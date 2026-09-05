# Adding a Paper Implementation

This guide walks through implementing a rendering paper in RenderLab.
The canonical example is the deferred pipeline: pure pass modules in `src/RenderLab.Papers/` (`GBufferPass.cs`, `DeferredLighting.cs`, `TonemapPass.cs`, `DebugVizPass.cs`) wired into `src/RenderLab.Pipelines/DeferredPipeline.cs`.

For how projects, pipelines, and the `Application` composition root fit together, see [`PROJECT-MODEL.md`](PROJECT-MODEL.md).

## Anatomy of a paper

A paper implementation is split across two assemblies:

| Lives in | What it owns |
|---|---|
| `src/RenderLab.Papers/<YourPass>.cs` | Pure: a `*PushConstants` builder + a `Record` function that takes a `*PassResources` value record. No fields, no `GpuState` mutation. |
| `src/RenderLab.Pipelines/<YourPipeline>.cs` | Impure shell: an `IPipeline` implementation owning render-pass / pipeline / framebuffer / descriptor lifetimes and the render-graph wiring. |

Pass modules are static classes: they describe *how* to record commands, never *what* GPU resources exist.
The pipeline owns lifetimes and hands them in by value.

The pipeline does not own a window, a swapchain, or a UI.
`Application` owns those and hands the pipeline a `RenderTarget` sized to the editor's viewport panel each frame.

## Step-by-step

### 1. Write your shaders

Add `.vert` / `.frag` files under `src/RenderLab.Shaders/<your-shader-name>/` (one folder per shader name; the build script discovers them recursively).
Compile to SPIR-V:

```bash
python src/RenderLab.Shaders/compile_shaders.py
```

The compiled `.spv` files are copied next to the app binary at build time.
Once the engine is running, editing a shader source recompiles and swaps the pipeline in place; see [`HOT-SHADER-RELOAD.md`](HOT-SHADER-RELOAD.md).

### 2. Define your push constants

Add a `[StructLayout(LayoutKind.Sequential)]` struct to `src/RenderLab.Gpu/PushConstants.cs` (or alongside your pass module if it's paper-specific).
Match the GLSL `layout(push_constant)` block exactly.

### 3. Write a pass module under `RenderLab.Papers`

Mirror the shape of `Papers/GBufferPass.cs`:

```csharp
namespace RenderLab.Papers;

public static class MyPass
{
    public static MyPushConstants BuildPushConstants(/* immutable scene inputs */) =>
        new() { /* ... */ };

    public static unsafe void Record(
        Vk vk,
        CommandBuffer cb,
        MyPassResources r,
        MyPushConstants pc /*, any per-frame resources passed in by value */)
    {
        // CmdBeginRenderPass / BindPipeline / SetViewport / SetScissor /
        // PushConstants / Bind buffers / Draw / EndRenderPass
    }
}

public readonly record struct MyPassResources(
    RenderPass RenderPass,
    Framebuffer Framebuffer,
    Pipeline Pipeline,
    PipelineLayout PipelineLayout,
    Extent2D Extent /*, any descriptor sets the pass binds */);
```

Keep `BuildPushConstants` pure, since it is the part that gets unit-tested.
`Record` is the only side-effecting code in the file, and it touches only the Vulkan handles passed in via `*PassResources`.

### 4. Declare the pass in the render graph

In your pipeline's `Initialize`, add a `RenderPassDeclaration` describing resource I/O.
The graph compiler topo-sorts passes and inserts barriers from these declarations alone.

```csharp
var myInput  = ResourceName.Of("Previous.Output");
var myOutput = ResourceName.Of("MyPass.Output");

var passes = ImmutableArray.Create(
    /* ...existing passes... */
    new RenderPassDeclaration("MyPass",
        Inputs:  [new PassInput(myInput, ResourceUsage.ShaderRead)],
        Outputs: [new PassOutput(myOutput, ResourceUsage.ColorAttachmentWrite)])
);

resolvedPasses = RenderGraphCompiler.Compile(passes).Match(
    ok: r => r,
    error: e => throw new InvalidOperationException($"Compile failed: {e}"));
```

### 5. Create resources and wire the recorder

`Initialize(gpu, assets)` creates what outlives a resize: render passes, descriptor set layouts, and the `VkPipeline` itself (`VulkanPipeline.CreateFullscreenPipeline` or `CreateGBufferPipeline`).
`RecreateTransient(gpu, target)` creates what is sized to the viewport: offscreen images (`VulkanImage.CreateOffscreen`), framebuffers (`VulkanPipeline.CreateOffscreenFramebuffer`), and the descriptor sets bound to those views (`VulkanDescriptors`).
It is called on startup and on every viewport resize, with the device already idle, and it must hang the final pass's framebuffer on the target's own view.

Then hand the recorder to `VulkanGraphExecutor` each frame from `RecordFrame`:

```csharp
var resourceImages = new Dictionary<ResourceName, Image>
{
    [myInput]  = previousPassImage,
    [myOutput] = myImage,
    /* ... */
};

var passRecorders = new Dictionary<string, Action<Vk, CommandBuffer>>
{
    /* ... */
    ["MyPass"] = (api, cb) => RecordMyPass(api, cb),
};

VulkanGraphExecutor.Execute(gpu, cmd, resolvedPasses, passRecorders, resourceImages);
```

`RecordMyPass` builds the per-frame `MyPassResources` and `MyPushConstants` from the pipeline's owned state, then forwards to `MyPass.Record`.
See `RecordGBufferPass` in `Pipelines/DeferredPipeline.cs` for the canonical shape.
`RecordFrame` must leave the target readable by a shader, because the editor draws it as a picture.

### 6. (Optional) Add a dedicated pipeline and starter project

If your paper deserves its own narrative, add a `Pipelines/MyPaperPipeline.cs` implementing `IPipeline` and one `Register` line in `Program.cs`:

```csharp
var registry = new PipelineRegistry()
    /* ...existing pipelines... */
    .Register("my-paper", () => new MyPaperPipeline());
```

Then create `projects/my-paper/` with a `project.json` naming that id, an `assets/` folder, and a starter scene.
See [`PROJECT-MODEL.md`](PROJECT-MODEL.md) for the manifest schema and the rest of the project contract.

### 7. (Optional) Expose controls

A pipeline never draws its own interface.
If your paper needs a tweakable parameter, put it on `UiModel` in `RenderLab.Ui` with a reducer case, and add or extend a panel in `RenderLab.Editor`.
That keeps the control somewhere the editor can see it, and keeps a UI framework out of the dependencies of a rendering technique.

If your pipeline cannot resolve every `VisualizationMode`, override `SupportedVisualizations` so the Visualization panel only offers the modes you can actually draw.

## What the engine gives you

- **Execution ordering**: `RenderGraphCompiler` topologically sorts passes by `ResourceName` dependencies.
- **Pipeline barriers**: resource transitions are computed from `PassInput` / `PassOutput` usage and inserted by `VulkanGraphExecutor`.
- **GPU timestamps**: wrap your recorder body in `timestamps.BeginPass(…)` / `timestamps.EndPass(…)` (`GpuTimestamps` in `RenderLab.Gpu.Debug`) for per-pass timings.
- **Editor debug panels**: return those timings and the resolved pass list from `GetFrameStats`, and the GPU Timings and Render Graph panels pick them up with no further wiring.
- **Shader hot reload**: implement `ReloadShaders` and a saved `.frag` recompiles and swaps without a restart.

## What you write yourself

- GLSL shaders and the SPIR-V build step.
- Vulkan pipeline + descriptor set layouts + offscreen images / framebuffers.
- Resource cleanup in `DestroyTransient` / `Dispose`, and recreation in `RecreateTransient` on viewport resize.

## Reference patterns in the deferred pipeline

| Pattern | Where |
|---|---|
| Geometry pass with push-constant matrices | `Papers/GBufferPass.cs` + `Pipelines/DeferredPipeline.RecordGBufferPass` |
| Fullscreen pass that reads previous outputs | `Papers/DeferredLighting.cs` + `Pipelines/DeferredPipeline.RecordLightingPass` |
| Per-frame SSBO upload (e.g. an array of lights) | `Scene/LightPacking.cs` + `Pipelines/DeferredPipeline.UploadLightsForCurrentFrame` |
| Fullscreen blit / tonemap | `Papers/TonemapPass.cs` + `Pipelines/DeferredPipeline.RecordTonemapPass` |
| Conditional debug visualization | `Papers/DebugVizPass.cs` + `Pipelines/DeferredPipeline.RecordTonemapPass` |
| Cross-pass depth-buffer sampling (manual barrier) | `TransitionDepthForSampling` / `TransitionDepthForAttachment` in `Pipelines/DeferredPipeline.cs` |
