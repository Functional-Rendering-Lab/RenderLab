# RenderLab — Product Requirement Document

**Version:** 0.1
**Author:** Andrés (TutanDev)
**Date:** 2026-04-08
**Status:** Draft

---

## 1. Purpose

RenderLab is a personal rendering laboratory for implementing, testing, and validating graphics research papers. It is not a game engine, not an editor, and not a production renderer. Every architectural decision serves one goal: **minimize the time between reading a paper and seeing its results on screen.**

---

## 2. Problem Statement

Implementing rendering papers today requires either:

- **C++ engines (Falcor, The Forge, bgfx):** high friction, slow iteration, language hostility for the author.
- **Unity/Unreal:** too much abstraction between you and the GPU. Pipeline state is hidden behind materials. No direct control over render graph topology, barrier placement, or resource aliasing.
- **Raw Vulkan/OpenGL from scratch:** weeks of boilerplate before drawing a triangle. Ceremony-to-insight ratio is brutal.
- **Existing F#/FP renderers (Aardvark):** correct architectural intuition but locked to F# and their own incremental computation model.

**Gap:** no lightweight, functional-core render testbed exists in modern C# that gives direct GPU control with minimal boilerplate and cross-platform reach (desktop + Android).

---

## 3. Goals

| ID | Goal | Success Criteria |
|----|------|------------------|
| G1 | **Paper-first workflow** | A new paper implementation requires only adding pure functions that produce render commands. No engine plumbing, no subclassing, no pipeline reconfiguration. |
| G2 | **Functional Core / Imperative Shell** | All paper logic, pass composition, and render graph compilation are pure functions with no side effects. GPU mutation is confined to a single submission boundary. |
| G3 | **Vulkan backend via Silk.NET** | Direct Vulkan API access through Silk.NET bindings. No intermediate abstraction that hides pipeline state, synchronization, or resource transitions. |
| G4 | **Desktop + Android** | Same paper code runs on desktop (Windows/Linux) and Android (Meta Quest, phones). Platform delta is limited to surface creation and device capability queries. |
| G5 | **wgpu-ready architecture** | The Gpu module boundary is designed so that a wgpu-native backend can be added later without changing any pure layer above it. |
| G6 | **Zero-allocation command recording** | Render commands are value types recorded into pooled buffers. Frame-to-frame steady state produces no GC pressure. |
| G7 | **Minimal scope** | No asset pipeline, no editor, no ECS, no physics, no audio. Meshes load from OBJ/glTF. Textures load from KTX2/PNG. That's it. |

---

## 4. Non-Goals

- **Production-quality renderer.** Performance matters for profiling papers, not for shipping games.
- **Material system.** Papers define their own shaders and pipeline state directly.
- **Scene editor or GUI tooling.** ImGui debug overlays only.
- **Shader hot-reload at launch.** Desirable later but not blocking.
- **Multi-GPU or ray tracing extensions.** Out of scope for v1.

---

## 5. Architecture

### 5.1 Layer Diagram

```
┌──────────────────────────────────────────┐
│         Paper Implementations            │  PURE
│  (each paper = module returning passes)  │
├──────────────────────────────────────────┤
│            Render Graph                  │  PURE
│  (DAG compilation, barrier insertion,    │
│   resource lifetime inference, aliasing) │
├──────────────────────────────────────────┤
│             Gpu Module                   │  IMPURE (thin shell)
│  (opaque handles, pooled resources,      │
│   command translation, submission)       │
├──────────────────────────────────────────┤
│              Platform                    │  IMPURE (thin shell)
│  (window/surface, input polling, loop)   │
└──────────────────────────────────────────┘
```

### 5.2 Purity Boundary

The architectural invariant is:

> **Everything above the Gpu Module is a pure function from data to data.
> Everything at and below the Gpu Module is the only code that performs side effects.**

This means:

- Paper implementations receive immutable input (scene snapshot, camera, resources) and return immutable output (render commands, resource declarations).
- The render graph compiler takes a set of passes and returns an ordered list of resolved passes with barriers. No mutation.
- Only `Gpu.Submit()` translates commands into Vulkan API calls.

### 5.3 Data Flow Per Frame

```
Scene snapshot (immutable)
  │
  ▼
Paper passes: Scene → ImmutableArray<RenderPass>        ← pure
  │
  ▼
Graph compile: passes → ImmutableArray<ResolvedPass>    ← pure (topo-sort, barriers, aliasing)
  │
  ▼
Command collect: resolved passes → Span<RenderCommand>  ← pure (flat command buffer)
  │
  ▼
Gpu.Submit(commands)                                    ← IMPURE BOUNDARY
  │
  ▼
Silk.NET Vulkan calls → GPU
```

---

## 6. Core Abstractions

### 6.1 Render Commands

A closed set of GPU operations represented as a tagged value type (discriminated union via struct + tag byte). No heap allocation. Exhaustive matching enforced via `Match<T>(...)`.

**Command types (v1):**

- `ClearColor` — clear a color attachment
- `ClearDepth` — clear a depth attachment
- `SetPipeline` — bind a graphics or compute pipeline
- `SetVertexBuffer` — bind vertex buffer at slot
- `SetIndexBuffer` — bind index buffer
- `SetDescriptorSet` — bind a descriptor set at index
- `PushConstants` — upload push constant data
- `DrawIndexed` — indexed draw call
- `Dispatch` — compute dispatch
- `CopyBufferToImage` — staging uploads
- `Blit` — image-to-image copy with format conversion

### 6.2 Handles

Opaque, typed indices into GPU-side pools. No pointers, no classes, no GC-tracked references to GPU objects. Handles are inert data — creating one does nothing; only passing it to `Gpu.*` methods causes side effects.

**Handle types:** `BufferHandle`, `ImageHandle`, `SamplerHandle`, `PipelineHandle`, `DescriptorSetHandle`, `ShaderModuleHandle`.

Each is a `readonly record struct` wrapping a `uint` index and a `uint` generation counter for use-after-free detection in debug builds.

### 6.3 Descriptors (Immutable Configuration Records)

All GPU resource creation goes through immutable descriptor records:

- `BufferDesc` — size, usage flags, memory location
- `ImageDesc` — dimensions, format, usage, mip levels, sample count
- `SamplerDesc` — filtering, addressing, anisotropy
- `GraphicsPipelineDesc` — shader modules, vertex layout, raster state, depth state, color formats, blend state
- `ComputePipelineDesc` — shader module, push constant layout, descriptor set layouts
- `RenderPassDesc` — color/depth attachments, load/store ops, formats

### 6.4 GpuState

The single mutable kernel. Contains:

- `Silk.NET.Vulkan.Vk` instance
- Physical device, logical device, queues
- VMA allocator handle
- Handle pools (buffer pool, image pool, pipeline pool, etc.)
- Frame synchronization primitives (fences, semaphores per frame-in-flight)
- Swapchain state
- Descriptor pool

Passed explicitly by `ref` — never global, never static, never ambient.

### 6.5 Render Graph

**Input:** `ImmutableArray<RenderPass>` where each pass declares:

- Named resource reads (handle + expected state)
- Named resource writes (handle + output state)
- An `Execute` function: `PassResources → ImmutableArray<RenderCommand>`

**Output:** `ImmutableArray<ResolvedPass>` where each resolved pass includes:

- The original pass
- Barrier commands to insert before execution
- Resource aliasing decisions (transient resources sharing memory)

**Algorithm:** topological sort on resource dependencies, then linear scan for lifetime analysis and barrier placement. Reference: Frostbite's FrameGraph (Wihlidal, GDC 2017).

---

## 7. Technology Stack

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Language | C# 13 / .NET 9 | Modern language features (records, pattern matching, static abstracts, spans). Author's primary language. |
| GPU bindings | Silk.NET Vulkan | Raw Vulkan bindings, no abstraction overhead. Identical API surface on desktop and Android. |
| Windowing (desktop) | Silk.NET GLFW | Flat C-style API, easy to wrap as a poll loop. Avoids OOP event callback patterns. |
| Windowing (Android) | NativeActivity + ANativeWindow | Minimal Java/Kotlin surface. .NET runs via NativeAOT as a native `.so`. |
| Shader compilation | glslc / dxc (subprocess) | GLSL/HLSL → SPIR-V. Called at build time or on-demand. No runtime compiler dependency. |
| Memory allocation | VMA (Vulkan Memory Allocator) via P/Invoke | Industry standard. Small C API surface for interop. |
| Debug UI | cimgui via P/Invoke | ImGui's C bindings. Flat API. Render through the engine's own Vulkan backend. |
| Mesh loading | Minimal OBJ parser + cgltf via P/Invoke | Enough to load paper test scenes. Not a full asset pipeline. |
| Texture loading | KTX2 (libktx P/Invoke) + stb_image | GPU-compressed formats for Android, uncompressed for desktop iteration. |
| Math | System.Numerics or custom | `Vector3`, `Matrix4x4`, `Quaternion` from BCL. Extend if needed. |
| Functional library | RenderLab.Functional (custom) | `Optional<T>`, `Result<T,E>`, tagged union base, `Pipe`, `Seq` extensions. |

### 7.1 wgpu Future Path

The Gpu module's public surface (handles, descriptors, `Submit`) is designed to be backend-agnostic. A future `RenderLab.Gpu.Wgpu` implementation would:

- Replace `Silk.NET.Vulkan` calls with `wgpu-native` P/Invoke calls.
- Map handle pools to wgpu object lifetimes.
- Translate `RenderCommand` spans into wgpu render/compute pass encoders.
- Eliminate manual barrier insertion (wgpu handles transitions internally).

**No code above the Gpu module changes.** The render graph still compiles passes. Papers still produce commands. Only the translation to GPU API calls differs.

---

## 8. Platform Matrix

| Platform | API | Surface | Runtime | Deployment |
|----------|-----|---------|---------|------------|
| Windows | Vulkan 1.3 | GLFW (`VK_KHR_win32_surface`) | .NET 9 | Self-contained executable |
| Linux | Vulkan 1.3 | GLFW (`VK_KHR_xcb_surface`) | .NET 9 | Self-contained executable |
| Android | Vulkan 1.1+ | `VK_KHR_android_surface` | NativeAOT `.so` | APK via .NET Android workload |

### 8.1 Mobile Constraints Record

Papers targeting Android receive a `DeviceCapabilities` record:

```
maxDescriptorSets, maxColorAttachments, maxComputeWorkGroupSize,
supportsGeometryShader (false on mobile), supportsTessellation,
maxSamplersPerStage, subgroupSize, etc.
```

Papers query this record — they never call Vulkan directly.

---

## 9. Project Structure

```
RenderLab.sln
├── src/
│   ├── RenderLab.Functional/        Optional<T>, Result<T,E>, unions, Pipe, Seq
│   ├── RenderLab.Gpu/               Handles, descriptors, GpuState, Gpu module
│   │                                 (Silk.NET Vulkan calls live here and only here)
│   ├── RenderLab.Graph/             RenderPass, RenderGraph compiler, barrier logic
│   ├── RenderLab.Platform.Desktop/  GLFW window, surface creation, main loop
│   ├── RenderLab.Platform.Android/  NativeActivity host, surface creation
│   ├── RenderLab.Scene/             Mesh, Camera, Transform — immutable records
│   ├── RenderLab.Shaders/           GLSL/HLSL sources, build-time SPIR-V compilation
│   ├── RenderLab.Debug/             ImGui integration, stats overlay, GPU timers
│   ├── RenderLab.Papers/            Paper implementations as static modules
│   │   ├── ClearScreen.cs           Milestone 0: validate full chain
│   │   ├── ForwardLit.cs            Milestone 1: basic forward pass
│   │   ├── DeferredGBuffer.cs       Milestone 2: multi-attachment
│   │   └── ...                      Each paper adds a file, not plumbing
│   └── RenderLab.App/              Composition root: wires everything
└── tests/
    ├── RenderLab.Graph.Tests/       Render graph compilation is pure → fully testable
    └── RenderLab.Functional.Tests/  Core library tests
```

---

## 10. Milestones

### M0 — Triangle of Truth
**Deliverable:** Clear screen to a solid color via the full chain.
**Validates:** GLFW → Vulkan surface → device → swapchain → command buffer → present.
**Scope:** Platform + Gpu module only. No render graph yet.

### M1 — First Pass
**Deliverable:** Indexed triangle drawn via a `GraphicsPipeline`, with vertex/fragment shaders compiled from GLSL to SPIR-V.
**Validates:** Pipeline creation, buffer upload, shader module loading, draw command translation.

### M2 — Render Graph Online
**Deliverable:** Two-pass rendering (geometry → post-process blit) driven by the render graph compiler.
**Validates:** Graph compilation, barrier insertion, transient resource creation, multi-pass command sequencing.

### M3 — Deferred Baseline
**Deliverable:** GBuffer pass (position, normal, albedo) → lighting pass → tonemap. Loaded OBJ mesh. Debug ImGui overlay showing GPU timings.
**Validates:** Multiple color attachments, descriptor sets, push constants, compute or fullscreen-quad lighting, ImGui integration.

### M4 — First Paper
**Deliverable:** Implement one concrete paper (candidate: SSAO — Horizon-Based Ambient Occlusion, Bavoil & Sainz 2008). Compare output against reference images from the paper.
**Validates:** The full workflow. A paper author adds a file, writes pure functions, sees results.

### M5 — Android Port
**Deliverable:** M3 (deferred baseline) running on an Android device via NativeAOT.
**Validates:** Surface creation, mobile Vulkan path, DeviceCapabilities gating, APK packaging.

---

## 11. Constraints and Decisions

| Decision | Choice | Alternatives Considered | Reason |
|----------|--------|------------------------|--------|
| Single language | C# only | F# for pure layer | Team familiarity. Custom functional library covers the gap. Avoids multi-language build complexity. |
| Vulkan only | No OpenGL/ES fallback | Silk.NET OpenGL for simpler start | Uniform API. Avoids maintaining two code paths. Android Vulkan coverage is sufficient (Vulkan 1.1 required since Android 10). |
| No abstraction layer over Vulkan | Papers see Vulkan-level concepts (pipelines, descriptors, barriers) | wgpu-style simplified API | Papers reference Vulkan concepts directly. Abstraction would require constant translation. wgpu backend planned as a future addition, not a replacement. |
| Value-type commands | `readonly record struct` with tag | Class hierarchy, interface dispatch | Zero allocation. Cache-friendly. Span-compatible. Matches functional union semantics. |
| Build-time shader compilation | glslc subprocess | Runtime compilation via shaderc | Simpler dependency. Faster startup. SPIR-V embedded or loaded from disk. Runtime variant selection via specialization constants, not recompilation. |
| VMA for memory | P/Invoke to C library | Manual Vulkan memory management | Not where insight lives. VMA is battle-tested. Small interop surface. |

---

## 12. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| NativeAOT on Android is immature for Vulkan workloads | M5 blocked | Fallback to Mono runtime. Validate early with a minimal Vulkan triangle on Android before M5. |
| Silk.NET Vulkan bindings have gaps or bugs | Gpu module blocked | Pin Silk.NET version. Patch locally if needed. Bindings are auto-generated from vk.xml — coverage is near-complete. |
| VMA P/Invoke interop complexity | Gpu module delayed | Use existing community bindings or generate from VMA's C API header. Small surface (~20 functions needed). |
| Scope creep into engine features | Project stalls | This document is the scope. If it's not in the milestones, it doesn't exist. |
| Render graph over-engineering | Premature abstraction | Start with linear pass ordering in M1. Add topological sort in M2. Add aliasing only when a paper needs transient resources. |

---

## 13. Success Definition

The project succeeds when:

1. A new paper implementation requires creating **one file** containing **pure functions** that produce render passes.
2. The same paper code compiles and runs on **desktop and Android** without `#if` directives in the paper file.
3. Time from "paper PDF open" to "first incorrect pixels on screen" is **under one evening session**.
4. The render graph is **fully unit-testable** without a GPU — because it's pure.

---

*This document is the scope. Features not listed here do not exist.*
