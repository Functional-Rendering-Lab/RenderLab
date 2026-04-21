# Adding a Paper Implementation

This guide walks through implementing a rendering paper in RenderLab.
The existing M3 deferred pipeline in `Program.cs` serves as the canonical example.

## Overview

A paper implementation consists of:

1. **GLSL shaders** compiled to SPIR-V (`src/RenderLab.Shaders/`)
2. **Pass declarations** — pure data describing resource I/O (`RenderPassDeclaration`)
3. **Pipeline setup** — Vulkan pipelines and descriptor set layouts
4. **Pass recorders** — functions that record Vulkan commands into a command buffer
5. **Wiring** — connecting declarations and recorders in `Program.cs`

The render graph compiler handles execution ordering and barrier insertion automatically.

## Step-by-Step

### 1. Write your shaders

Add `.vert` and `.frag` files to `src/RenderLab.Shaders/<your-shader-name>/`
(one folder per shader name — the compile script discovers them recursively).
Compile them to SPIR-V:

```bash
python src/RenderLab.Shaders/compile_shaders.py
```

### 2. Declare your passes

Each pass declares what resources it reads and writes. This is pure data — no execution.

```csharp
var myInput  = new ResourceName("MyPass.Input");
var myOutput = new ResourceName("MyPass.Output");

var myPass = new RenderPassDeclaration("MyPass",
    Inputs:  [new PassInput(myInput, ResourceUsage.ShaderRead)],
    Outputs: [new PassOutput(myOutput, ResourceUsage.ColorAttachmentWrite)]);
```

Add your pass to the `ImmutableArray.Create(...)` call alongside existing passes (Program.cs:136).
The compiler will sort it into the correct execution position based on dependencies.

### 3. Create the pipeline

Use `VulkanPipeline` helpers depending on your pass type:

- **Fullscreen pass** (post-process, lighting): `VulkanPipeline.CreateFullscreenPipeline()`
- **Geometry pass** (drawing meshes): `VulkanPipeline.CreateGBufferPipeline()` or similar

If your pass reads from previous passes, create a descriptor set layout via `VulkanDescriptors`.

### 4. Write the recorder function

A recorder receives `(Vk api, CommandBuffer cb)` and records Vulkan commands:

```csharp
unsafe void RecordMyPass(Vk api, CommandBuffer cb)
{
    // Begin render pass with your framebuffer
    api.CmdBeginRenderPass(cb, &renderPassBegin, SubpassContents.Inline);
    api.CmdBindPipeline(cb, PipelineBindPoint.Graphics, myPipeline);

    // Set viewport + scissor, bind descriptors, push constants, draw
    api.CmdDraw(cb, 3, 1, 0, 0); // fullscreen triangle

    api.CmdEndRenderPass(cb);
}
```

### 5. Wire into the graph executor

Add your recorder to the `passRecorders` dictionary (Program.cs:213):

```csharp
passRecorders["MyPass"] = (api, cb) => RecordMyPass(api, cb);
```

Map any new resources to Vulkan images in `resourceImages` (Program.cs:203).

## What You Get for Free

- **Execution ordering** — the compiler topologically sorts passes by resource dependencies
- **Pipeline barriers** — resource transitions are computed and inserted automatically
- **GPU timestamps** — call `timestamps.BeginPass()`/`EndPass()` for per-pass profiling
- **ImGui overlay** — timings display automatically in the debug window

## What You Must Do Yourself

- Compile shaders to SPIR-V
- Create Vulkan pipelines and descriptor set layouts
- Create offscreen images and framebuffers for your pass outputs
- Destroy resources on cleanup and swapchain resize

## Common Patterns

### Fullscreen pass (reads texture, writes to render target)

See `RecordLightingPass` and `RecordTonemapPass` in Program.cs:309 and :355.
Uses `VulkanPipeline.CreateFullscreenPipeline()` + `CmdDraw(3, 1, 0, 0)` for a fullscreen triangle.

### Geometry pass with push constants

See `RecordGBufferPass` in Program.cs:259.
Uses `CmdPushConstants()` for per-draw data (model/view/projection matrices).

### Reading from a previous pass

Create a descriptor set that binds the previous pass's output image view + sampler.
See how the lighting pass reads GBuffer textures via `gbufferDescSets` (Program.cs:335).

## Current State

The `RenderLab.Papers/` module from the PRD does not yet exist.
Paper logic currently lives directly in `Program.cs`. As more papers are added,
pass declarations and recorders will be extracted into standalone modules.
