# Hot Shader Reload

Edit a `.vert` / `.frag` under `src/RenderLab.Shaders/`, save, and see the new pipeline running within a frame or two. Scene state, camera, and selection survive the reload.

## How it works

1. `ShaderHotReload` (in `RenderLab.Gpu`) constructs at startup. It walks parent directories from `AppContext.BaseDirectory` looking for `src/RenderLab.Shaders/`, and looks for `glslc` on `PATH`. If either is missing, hot reload disables itself and logs once — the app runs normally.
2. A `FileSystemWatcher` (recursive, `*.vert` + `*.frag`) enqueues changed paths. The pump runs once per frame from `Application.Loop`, drains the queue after a 120 ms quiet window, and for each path runs `glslc <src> -o <baseDir>/shaders/<name>.spv`, capturing stderr.
3. If every compile succeeds, `vkDeviceWaitIdle` + `pipeline.ReloadShaders(gpu)` rebuilds the affected `VkPipeline`/`PipelineLayout` handles. Compile failures keep the previous pipeline alive and log `glslc` stderr.
4. **F5** queues every shader in the tree (force reload).

All log lines go to stdout with a `  shader:` prefix.

## What gets reloaded

- Triangle, GBuffer, and Deferred pipelines all implement `IPipeline.ReloadShaders`. They reuse their existing render passes, descriptor set layouts, descriptor sets, and offscreen images — only the `VkPipeline` + `VkPipelineLayout` are destroyed and recreated.

## What does NOT get reloaded

- **Descriptor set layout changes** (new bindings, changed types). The pipeline layout would diverge from the descriptor sets bound at record time; results undefined. Restart the app.
- **Push-constant range changes.** Same reason.
- **Render pass attachment-format changes.** Pipelines are built against a render pass handle that is *not* recreated.
- **Vertex input layout changes.** The binding/attribute descriptions live in the C# pipeline code, not in the shader.

No warning is emitted for these — the rebuild succeeds, but rendering may go wrong. If something looks off after a reload, restart the app.

## Requirements

- `glslc` on `PATH` (Vulkan SDK).
- The running binary must be able to walk up to the repo (so a published build outside the repo will not hot-reload — this is intentional; the feature is a debug-loop accelerator, not a runtime feature).

## Why a callback, not a pipeline reference

`RenderLab.Gpu` cannot depend on `RenderLab.Pipelines` (Pipelines references Gpu, not the other way around). `ShaderHotReload` exposes an `Action<GpuState>? OnReload`; `Application` sets it to `g => pipeline.ReloadShaders(g)` after each `LoadProject` and clears it on project teardown.
