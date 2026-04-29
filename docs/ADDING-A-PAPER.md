# Adding a Paper Implementation

This guide walks through implementing a rendering paper in RenderLab. The canonical
example is the M3 deferred pipeline: pure pass modules in `src/RenderLab.Papers/`
(`GBufferPass.cs`, `DeferredLighting.cs`, `TonemapPass.cs`, `DebugVizPass.cs`)
wired into a demo class in `src/RenderLab.App/Demos/DeferredDemo.cs`.

For the rationale behind the per-article demo split, see
[`DEMO-ARCHITECTURE.md`](DEMO-ARCHITECTURE.md).

## Anatomy of a paper

A paper implementation is split across two assemblies:

| Lives in | What it owns |
|---|---|
| `src/RenderLab.Papers/<YourPass>.cs` | Pure: a `*PushConstants` builder + a `Record` function that takes a `*PassResources` value record. No fields, no `GpuState` mutation. |
| `src/RenderLab.App/Demos/<YourDemo>.cs` | Impure shell: owns the window, `GpuState`, render-pass / pipeline / framebuffer / descriptor lifetimes, and the render-graph wiring. |

Pass modules are static classes — they describe *how* to record commands, never
*what* GPU resources exist. The demo owns lifetimes and hands them in by value.

## Step-by-step

### 1. Write your shaders

Add `.vert` / `.frag` files under `src/RenderLab.Shaders/<your-shader-name>/`
(one folder per shader name — the build script discovers them recursively).
Compile to SPIR-V:

```bash
python src/RenderLab.Shaders/compile_shaders.py
```

The compiled `.spv` files are copied next to the demo binary at build time.

### 2. Define your push constants

Add a `[StructLayout(LayoutKind.Sequential)]` struct to
`src/RenderLab.Gpu/PushConstants.cs` (or alongside your pass module if it's
paper-specific). Match the GLSL `layout(push_constant)` block exactly.

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

Keep `BuildPushConstants` pure — it's the part that gets unit-tested. `Record`
is the only side-effecting code in the file, and it touches only the Vulkan
handles passed in via `*PassResources`.

### 4. Declare the pass in the render graph

In your demo, add a `RenderPassDeclaration` describing resource I/O. The graph
compiler topo-sorts passes and inserts barriers from these declarations alone.

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

In your demo's `Init` and `CreateTransientResources`, create the pipeline
(`VulkanPipeline.CreateFullscreenPipeline` or `CreateGBufferPipeline`),
descriptor sets (`VulkanDescriptors`), and offscreen images / framebuffers
(`VulkanImage.CreateOffscreen`, `VulkanPipeline.CreateOffscreenFramebuffer`).

Then hand the recorder to `VulkanGraphExecutor` each frame:

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

`RecordMyPass` builds the per-frame `MyPassResources` and `MyPushConstants` from
the demo's owned state, then forwards to `MyPass.Record`. See `RecordGBufferPass`
in `Demos/DeferredDemo.cs` for the canonical shape.

### 6. (Optional) Add a dedicated demo

If your paper deserves its own narrative, add a `Demos/MyPaperDemo.cs`
implementing `IDemo` and one switch case in `Program.cs`. See
[`DEMO-ARCHITECTURE.md`](DEMO-ARCHITECTURE.md) for the rationale.

## What the engine gives you

- **Execution ordering** — `RenderGraphCompiler` topologically sorts passes by `ResourceName` dependencies.
- **Pipeline barriers** — resource transitions are computed from `PassInput` / `PassOutput` usage and inserted by `VulkanGraphExecutor`.
- **GPU timestamps** — wrap your recorder body in `timestamps.BeginPass(…)` / `timestamps.EndPass(…)` for per-pass timings.
- **ImGui debug overlay** — timings and the resolved pass list show up automatically via `RenderGraphDebugMenu`.

## What you write yourself

- GLSL shaders and the SPIR-V build step.
- Vulkan pipeline + descriptor set layouts + offscreen images / framebuffers.
- Resource cleanup in `DestroyTransientResources` / `Dispose` and recreation on swapchain resize.

## Reference patterns in the deferred demo

| Pattern | Where |
|---|---|
| Geometry pass with push-constant matrices | `Papers/GBufferPass.cs` + `Demos/DeferredDemo.RecordGBufferPass` |
| Fullscreen pass that reads previous outputs | `Papers/DeferredLighting.cs` + `Demos/DeferredDemo.RecordLightingPass` |
| Fullscreen blit / tonemap | `Papers/TonemapPass.cs` + `Demos/DeferredDemo.RecordTonemapPass` |
| Conditional debug visualization | `Papers/DebugVizPass.cs` + `Demos/DeferredDemo.RecordTonemapPass` |
| Cross-pass depth-buffer sampling (manual barrier) | `TransitionDepthForSampling` / `TransitionDepthForAttachment` in `Demos/DeferredDemo.cs` |
