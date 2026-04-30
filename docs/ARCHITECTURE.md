# RenderLab Architecture

A rendering research lab for implementing graphics papers with minimal friction.
For goals, milestones, and design rationale see [RenderLab-PRD.md](../RenderLab-PRD.md).

## Module Dependency Graph

```
RenderLab.App              (desktop composition root — wires everything)
  |-> RenderLab.Papers     (paper implementations — straddle pure/impure)
  |     |-> RenderLab.Scene      (Scene snapshot, Camera, Mesh, Light DU, HemisphericAmbient, MaterialParams — pure data)
  |     '-> RenderLab.Gpu        (Vulkan bindings, handles, commands, state)
  |-> RenderLab.Gpu
  |     |-> RenderLab.Graph      (pure render graph compiler)
  |     '-> RenderLab.Functional (Optional, Result, Pipe)
  |-> RenderLab.Graph
  |-> RenderLab.Scene
  |-> RenderLab.Ui         (pure Elm-style state: Model/Msg/Update/Intent — no ImGui, no Vulkan)
  |-> RenderLab.Ui.ImGui   (imperative shell for RenderLab.Ui: ImGui views + GPU timestamps -> depends on Gpu, Ui)
  '-> RenderLab.Platform.Desktop  (GLFW window — no internal deps)
```

No circular dependencies. `Graph`, `Scene`, `Functional`, and `Ui` have zero side-effect dependencies (no Vulkan, no ImGui).

### Ui ↔ Ui.ImGui split

`RenderLab.Ui` holds the pure state layer — `AppUiModel`, `AppUiMsg`, `AppUiUpdate`, `UiIntent`, `VisualizationMode`, `DemoId`, `FrameStats`. It has no `ImGuiNET` or Vulkan references, so it can be unit-tested without a GPU (see `tests/RenderLab.Ui.Tests`).

`RenderLab.Ui.ImGui` is the imperative shell that *renders* that pure state with `ImGuiNET` and owns GPU-side debug plumbing (`VulkanImGui`, `GpuTimestamps`). Each debug panel (`AppMenuBar`, `LightingDebugMenu`, `FreeCameraDebugMenu`, `RenderGraphDebugMenu`, `SphereDebugMenu`, `VisualizationDebugMenu`, `ScenePanel`) reads an immutable slice of the model, draws ImGui widgets, and returns `UiIntent`s the shell folds back through `AppUiUpdate`. `ScenePanel` is read-only and inspects the per-frame `Scene` snapshot rather than `UiModel`.

The assembly is `RenderLab.Ui.ImGui` but the last namespace segment collides with `ImGuiNET.ImGui` (the ImGui entry-point class) during simple-name lookup. To keep the assembly name matching the folder, each file declares a `using ImGui = ImGuiNET.ImGui;` alias *inside* the namespace — compilation-unit aliases lose to the parent-namespace walk-up, so the alias must live after `namespace RenderLab.Ui.ImGui;`.

## Purity Boundary

Everything in `RenderLab.Graph` and `RenderLab.Scene` is pure — no side effects, no mutation, fully unit-testable without a GPU.

Everything in `RenderLab.Gpu` and `RenderLab.Platform.Desktop` performs side effects. `GpuState` is the single mutable kernel, passed explicitly by reference — never global, never static. `DeviceCapabilities` is an immutable record on `GpuState`, queried once at device creation — papers read it instead of calling Vulkan directly.

GPU memory flows through a single engine-owned surface: `Allocator` (`Gpu/Allocator.cs`), hung off `GpuState.Allocator`. Every `vkAllocateMemory` goes through it; resource creation returns `(handle, Allocation)` so buffer/memory lifetimes are coupled at the type level, and callers pick a `MemoryIntent` (`GpuOnly`, `CpuToGpu`) instead of hand-rolling memory-property flags. The ImGui per-frame vertex/index buffers grow in doubling steps and stay mapped for the lifetime of the instance, so `vkAllocateMemory` fires O(log N) times at warm-up rather than every resize. Sub-allocation stays on the roadmap for when it becomes a measurable bottleneck.

`Program.cs` (desktop) is a CLI dispatcher that selects a demo class from `Demos/` by name. Each demo is a self-contained composition root — it owns its window, GPU, render loop, and cleanup. See [`DEMO-ARCHITECTURE.md`](DEMO-ARCHITECTURE.md) for the rationale.

### Swapchain present mode (lab policy)

`VulkanSwapchain` deliberately picks `FIFO_RELAXED` over `MAILBOX` and uses exactly `capabilities.MinImageCount` (no `+1`). The AAA default — Mailbox plus an extra image — *hides* frame-time overruns by buffering ahead and replacing un-presented frames; in a research lab that signal is what we want to see. Under this configuration a frame that misses the 16.67 ms budget tears on that frame instead of being smoothed over, and the negotiated image count is logged at startup. `FIFO` (universally supported) is the fallback. See `blogs/ideas/field-notes/swapchain-present-mode/draft.md` for the full reasoning. Note: this couples CPU and GPU more tightly than a pipelined `+1` setup, so technique cost should be measured with GPU timestamp queries (`GpuTimestamps`), not wall-clock frame time.

## Per-Frame Data Flow

```
UiModel (editable per-frame state)
  |
  v
Scene snapshot ..................................... PURE
  Immutable record built each frame in the demo:
  Scene(Camera, ImmutableArray<SceneMesh>, ImmutableArray<Light>)
  Consumed by pass recorders and the Scene inspector panel.
  Render-config (shading mode, viz mode, clear color) stays on UiModel.
  |
  v
Pass declarations (Program.cs) ...................... PURE
  Each pass declares resource I/O as RenderPassDeclaration
  |
  v
RenderGraphCompiler.Compile() ...................... PURE
  Topological sort (Kahn's algorithm) + barrier insertion
  Output: Result<ImmutableArray<ResolvedPass>, GraphError>
  Errors: Cycle, DuplicateWriter, UnknownResource, InvalidResourceName
  |
  v
VulkanGraphExecutor.Execute() ...................... IMPURE BOUNDARY
  Inserts Vulkan pipeline barriers from ResolvedPass.BarriersBefore
  Calls per-pass recorder functions (e.g. DeferredLighting.Record)
  |
  v
VulkanFrame.EndFrame() ............................. GPU SUBMISSION
  Queue submit + present
```

### Deferred Pipeline (M3 → M5)

```
GBuffer pass        -> writes Position, Normal, Albedo (3 color attachments + depth)
                       Alpha channels carry material: Normal.a = specularStrength,
                       Albedo.a = shininess / 256
  |
Lighting pass       -> reads GBuffer textures via descriptor set 0, writes HDR image
                       Blinn-Phong: hemispheric ambient (sky/ground) + Lambertian
                       diffuse + specular, accumulated per light.
                       Material params unpacked from GBuffer alpha channels.
                       Per-frame lights (point + directional) live in a single
                       SSBO (set 1, binding 0) packed by LightPacking — points
                       first, then directionals; the shader branches on a type
                       tag in the entry. Push constants hold camera, shading
                       mode, light count, and the hemispheric ambient pair.
  |
Tonemap pass        -> reads HDR, writes to swapchain backbuffer
  |
ImGui overlay       -> renders debug stats on top (outside render graph)
```

## Key Abstractions

| Abstraction | Location | Purpose |
|---|---|---|
| `RenderPassDeclaration` | `Graph/GraphTypes.cs` | Declares a pass with named resource I/O |
| `RenderGraphCompiler` | `Graph/RenderGraphCompiler.cs` | Topological sort + barrier insertion (pure) |
| `ResolvedPass` | `Graph/GraphTypes.cs` | Compiler output: pass + computed barriers |
| `GpuState` | `Gpu/GpuState.cs` | Single mutable kernel for all Vulkan state |
| `Allocator` | `Gpu/Allocator.cs` | Engine's only GPU-memory allocation surface — intent-based, returns coupled `(handle, Allocation)` |
| `DeviceCapabilities` | `Gpu/DeviceCapabilities.cs` | Immutable device properties/features queried once at creation |
| `RenderCommand` | `Gpu/RenderCommand.cs` | Tagged union value type — zero heap allocation |
| `Handle types` | `Gpu/Handles.cs` | Opaque typed indices with generation counters |
| `VulkanGraphExecutor` | `Gpu/VulkanGraphExecutor.cs` | Translates resolved passes to Vulkan barriers + recordings |
| `DeferredLighting` | `Papers/DeferredLighting.cs` | Blinn-Phong lighting pass — pure push-constant builder + Vulkan recorder |
| `Light` | `Scene/Light.cs` | Discriminated union root for `PointLight` and `DirectionalLight` |
| `PointLight` | `Scene/PointLight.cs` | Immutable point light (position, color, intensity) |
| `DirectionalLight` | `Scene/DirectionalLight.cs` | Immutable directional light (unit direction, color, intensity) |
| `Direction` | `Scene/Direction.cs` | Smart-constructed unit-length 3D direction — rejects zero vector |
| `Intensity` | `Scene/Intensity.cs` | Smart-constructed non-negative scalar |
| `HemisphericAmbient` | `Scene/HemisphericAmbient.cs` | Sky/ground color pair feeding the hemispheric ambient term |
| `MaterialParams` | `Scene/MaterialParams.cs` | Blinn-Phong material (specular strength, shininess) — encoding matches GBuffer alpha |
| `GpuLight` | `Scene/GpuLight.cs` | std430 GPU layout of a light (paired with `Light` struct in `lighting.frag`); `PositionType.w` is the type tag |
| `LightPacking` | `Scene/LightPacking.cs` | Pure packer from the `Light` DU to `GpuLight` — partitions points-first then directionals |
| `Scene` / `SceneMesh` | `Scene/Scene.cs` | Per-frame immutable snapshot (camera, meshes, lights) consumed by pass recorders and the Scene inspector |

## Build and Run

```bash
# Prerequisites: .NET 9 SDK, Vulkan SDK (for glslc)

# Desktop — runs the default demo (deferred)
dotnet build src/RenderLab.App
dotnet run --project src/RenderLab.App

# Desktop — pick a specific demo (see docs/DEMO-ARCHITECTURE.md)
dotnet run --project src/RenderLab.App -- triangle   # Post 2
dotnet run --project src/RenderLab.App -- gbuffer    # Post 3
dotnet run --project src/RenderLab.App -- deferred   # Post 4

# Compile shaders (requires glslc on PATH)
python src/RenderLab.Shaders/compile_shaders.py

# Run tests (render graph compiler — no GPU required)
dotnet test tests/RenderLab.Graph.Tests
```

## Source Layout

```
src/
  RenderLab.Functional/        Optional<T>, Result<T,E>, Pipe extensions
  RenderLab.Graph/             RenderGraphCompiler, pass/barrier types
  RenderLab.Gpu/               Vulkan device, swapchain, buffers, images,
                               pipelines, descriptors, graph executor,
                               Allocator, DeviceCapabilities, PushConstants
  RenderLab.Scene/             Scene snapshot, Camera, MeshData, Vertex3D,
                               Light DU (PointLight, DirectionalLight),
                               Direction, Intensity, HemisphericAmbient,
                               MaterialParams, FreeCameraController, OBJ loader
  RenderLab.Platform.Desktop/  GLFW window wrapper (poll-based)
  RenderLab.Papers/            Pass modules: GBufferPass, DeferredLighting,
                               TonemapPass, DebugVizPass
  RenderLab.Ui/                Pure Elm-style UI state (Model/Msg/Update/Intent)
  RenderLab.Ui.ImGui/          Imperative shell for RenderLab.Ui: ImGui views + GPU timestamps
  RenderLab.Shaders/           GLSL sources + SPIR-V build script
  RenderLab.App/               CLI dispatcher + per-article demos under Demos/
tests/
  RenderLab.Functional.Tests/  Optional, Result, Pipe
  RenderLab.Graph.Tests/       Topological sort, barrier insertion, cycle detection
  RenderLab.Scene.Tests/       Camera math, free-fly controller, material packing
  RenderLab.Ui.Tests/          Pure UI reducers (Model/Msg/Update)
```
