# RenderLab Architecture

A rendering research lab for implementing graphics papers with minimal friction.
For goals, milestones, and design rationale see [RenderLab-PRD.md](../RenderLab-PRD.md).

## Module Dependency Graph

```
RenderLab.App              (desktop composition root — wires everything)
  |-> RenderLab.Papers     (paper implementations — straddle pure/impure)
  |     |-> RenderLab.Scene      (Camera, Mesh, PointLight, MaterialParams — pure data)
  |     '-> RenderLab.Gpu        (Vulkan bindings, handles, commands, state)
  |-> RenderLab.Gpu
  |     |-> RenderLab.Graph      (pure render graph compiler)
  |     '-> RenderLab.Functional (Optional, Result, Pipe)
  |-> RenderLab.Graph
  |-> RenderLab.Scene
  |-> RenderLab.Debug      (ImGui overlay, GPU timestamps -> depends on Gpu)
  '-> RenderLab.Platform.Desktop  (GLFW window — no internal deps)

RenderLab.Platform.Android (Android composition root — Activity + SurfaceView)
  |-> RenderLab.Gpu
  |-> RenderLab.Graph
  '-> RenderLab.Scene
```

No circular dependencies. `Graph`, `Scene`, and `Functional` have zero internal dependencies.

## Purity Boundary

Everything in `RenderLab.Graph` and `RenderLab.Scene` is pure — no side effects, no mutation, fully unit-testable without a GPU.

Everything in `RenderLab.Gpu`, `RenderLab.Platform.Desktop`, and `RenderLab.Platform.Android` performs side effects. `GpuState` is the single mutable kernel, passed explicitly by reference — never global, never static. `DeviceCapabilities` is an immutable record on `GpuState`, queried once at device creation — papers read it instead of calling Vulkan directly.

`Program.cs` (desktop) and `RenderLabActivity.cs` (Android) are composition roots that wire pure declarations to the impure shell.

## Per-Frame Data Flow

```
Scene snapshot (Camera, PointLight, mesh transforms)
  |
  v
Pass declarations (Program.cs) ...................... PURE
  Each pass declares resource I/O as RenderPassDeclaration
  |
  v
RenderGraphCompiler.Compile() ...................... PURE
  Topological sort (Kahn's algorithm) + barrier insertion
  Output: ImmutableArray<ResolvedPass>
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
Lighting pass       -> reads GBuffer textures via descriptor set, writes HDR image
                       Blinn-Phong: ambient + Lambertian diffuse + specular.
                       Material params unpacked from GBuffer alpha channels.
                       Currently single PointLight via push constants.
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
| `DeviceCapabilities` | `Gpu/DeviceCapabilities.cs` | Immutable device properties/features queried once at creation |
| `RenderCommand` | `Gpu/RenderCommand.cs` | Tagged union value type — zero heap allocation |
| `Handle types` | `Gpu/Handles.cs` | Opaque typed indices with generation counters |
| `VulkanGraphExecutor` | `Gpu/VulkanGraphExecutor.cs` | Translates resolved passes to Vulkan barriers + recordings |
| `DeferredLighting` | `Papers/DeferredLighting.cs` | Blinn-Phong lighting pass — pure push-constant builder + Vulkan recorder |
| `PointLight` | `Scene/PointLight.cs` | Immutable point light (position, color, intensity) |
| `MaterialParams` | `Scene/MaterialParams.cs` | Blinn-Phong material (specular strength, shininess) — encoding matches GBuffer alpha |

## Build and Run

```bash
# Prerequisites: .NET 9 SDK, Vulkan SDK (for glslc)

# Desktop
dotnet build src/RenderLab.App
dotnet run --project src/RenderLab.App

# Android (requires Android SDK + android workload)
dotnet build src/RenderLab.Platform.Android -c Release
dotnet build src/RenderLab.Platform.Android -c Release -t:Install  # build + install via adb

# Compile shaders (requires glslc on PATH)
python src/RenderLab.Shaders/compile_shaders.py

# Run tests (render graph compiler — no GPU required)
dotnet test tests/RenderLab.Graph.Tests
```

## Source Layout

```
src/
  RenderLab.Functional/       Optional<T>, Result<T,E>, Pipe extensions
  RenderLab.Graph/             RenderGraphCompiler, pass/barrier types
  RenderLab.Gpu/               Vulkan device, swapchain, buffers, images,
                               pipelines, descriptors, graph executor,
                               DeviceCapabilities, PushConstants
  RenderLab.Scene/             Camera, MeshData, Vertex3D, PointLight,
                               MaterialParams, OBJ loader
  RenderLab.Platform.Desktop/  GLFW window wrapper (poll-based)
  RenderLab.Platform.Android/  Activity + SurfaceView, ANativeWindow JNI,
                               deferred pipeline (GBuffer→Lighting→Tonemap)
  RenderLab.Papers/            Paper implementations (DeferredLighting)
  RenderLab.Debug/             ImGui integration, GPU timestamp queries
  RenderLab.Shaders/           GLSL sources + SPIR-V build script
  RenderLab.App/               Desktop composition root (Program.cs)
tests/
  RenderLab.Graph.Tests/       6 tests: topo-sort, barriers, cycle detection
```
