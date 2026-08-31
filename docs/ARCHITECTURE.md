# RenderLab Architecture

A rendering research lab for implementing graphics papers with minimal friction.
For goals, milestones, and design rationale see [RenderLab-PRD.md](../RenderLab-PRD.md).

## Module Dependency Graph

```
RenderLab.App              (desktop composition root — Application + Program)
  |-> RenderLab.Pipelines  (IPipeline + concrete pipelines: Triangle/GBuffer/Deferred + SceneLoader/SceneBuilder)
  |     |-> RenderLab.Project    (pure project + scene document model + JSON IO)
  |     |-> RenderLab.Papers     (paper implementations — straddle pure/impure)
  |     |     |-> RenderLab.Scene
  |     |     '-> RenderLab.Gpu
  |     |-> RenderLab.Ui.ImGui   (UiView + GpuTimestamps — used by DeferredPipeline)
  |     |-> RenderLab.Ui
  |     |-> RenderLab.Assets
  |     |-> RenderLab.Graph
  |     |-> RenderLab.Scene
  |     '-> RenderLab.Gpu
  |-> RenderLab.Gpu
  |     |-> RenderLab.Graph      (pure render graph compiler)
  |     |-> RenderLab.Assets
  |     '-> RenderLab.Functional (Optional, Result, Pipe)
  |-> RenderLab.Project    (read project.json + *.scene.json from disk)
  |-> RenderLab.Ui         (pure Elm-style state: Model/Msg/Update/Intent — no ImGui, no Vulkan)
  |-> RenderLab.Ui.ImGui   (imperative shell for RenderLab.Ui: ImGui views + GPU timestamps -> depends on Gpu, Ui)
  |-> RenderLab.Editor     (the Ptah view layer replacing Ui.ImGui -> depends on Gpu, Ui, Assets, Project + Ptah)
  '-> RenderLab.Platform.Desktop  (GLFW window — no internal deps)
```

No circular dependencies. `Graph`, `Scene`, `Functional`, and `Ui` have zero side-effect dependencies (no Vulkan, no ImGui).

### Ui ↔ Ui.ImGui split

`RenderLab.Ui` holds the pure state layer — `AppUiModel`, `AppUiMsg`, `AppUiUpdate`, `UiIntent`, `VisualizationMode`, `FrameStats`, plus `EditableDrawable` (the editor-side mirror of a scene `Drawable`). It has no `ImGuiNET` or Vulkan references, so it can be unit-tested without a GPU (see `tests/RenderLab.Ui.Tests`).

`RenderLab.Ui.ImGui` is the imperative shell that *renders* that pure state with `ImGuiNET` and owns GPU-side debug plumbing (`VulkanImGui`, `GpuTimestamps`). It is being replaced panel by panel by `RenderLab.Editor` (below), and what is left in it is `RenderGraphDebugMenu`, the main menu bar, and the dockspace that places the one panel still drawn through it.

Panels split into two roles, and the split survives the change of framework. **Outliners / browsers** list items and emit `UiMsg.Select` when one is clicked; the **`InspectorPanel`** switches on `UiModel.Selection` and renders the editor for the selected item — a drawable's transform/mesh/material, a light's fields, a material asset's parameters, a mesh or texture asset's import settings, or the `Camera` and `Environment` pseudo-items (ambient + clear color + background mode). Only the Inspector edits item properties; the Lighting panel keeps the shading-model and lighting-only toggles (render-mode, not item edits), and add / remove / rename / delete remain on the outliners as list operations. `Selection` is a discriminated union — `None | Drawable | Light | MaterialAsset | MeshAsset | TextureAsset | Environment | Camera` — so removing the currently-selected item collapses to a single reducer rule.

### RenderLab.Editor: the view layer replacing Ui.ImGui

`RenderLab.Editor` is the second view layer over the same `RenderLab.Ui`, built on Ptah instead of Dear ImGui, and the two run side by side while the migration is in flight. The phases and their current state are in `docs/plans/imgui-replacement-feasibility.md` in the Ptah repository, which is checked out beside this one.

It is named for what it is rather than for the library that draws it, and its `Draw` has the same shape as `UiView.Draw` — model in, `UiViewResult` out — so `Application` folds one kind of result whichever framework produced it, and the day the last panel moves the ImGui half is deleted rather than untangled.

- `PtahUi` is the counterpart of `VulkanImGui`: the font atlas, the `UIContext`, the input translation over the `IInputContext` `DesktopWindow` already owns, and the draw target. Ptah's Vulkan backend creates no device, no swapchain and no frame loop — it borrows handles and records into the command buffer `Application` is already recording, in its own instance of the overlay pass.
- `EditorLayout` is the shell: the three-column arrangement the dock ini settled on, written once in code, with the docking machinery left behind. It names every panel in its final place and `Ported` says which of them Ptah draws today; a panel that has not moved leaves a hole in the layout, and its ImGui window is still drawn there.
- `EditorView` holds the panel tree and dispatches to the ported panels. `EditorTheme` is RenderLab's palette written as a Ptah theme, and `WidgetState` holds the interface's own state — which drop-down is open, which colour picker — which is deliberately not in `UiModel`.
- The panels themselves are one file each: `GpuTimingsPanel`, `VisualizationPanel`, `LightingPanel`, `InspectorPanel`, `ScenePanel`, `AssetBrowserPanel`, `ProjectPanel`. `DebugFields` has no counterpart, because Ptah's `WidgetKit` is it.
- `AssetDialogs` holds the rename and delete dialogs, drawn once per frame from `WidgetState.Dialog` and beside the panel tree rather than inside the panel that opens them: a modal dims the whole window and takes the mouse and keyboard from everything behind it, including that panel. Dear ImGui's popups are opened by id at the row's own call site, so the ImGui version built both dialogs once per asset and needed a dictionary of drafts keyed by guid; here, whether a dialog is up is application state and there is one of it.
- Adding a project mesh to the scene is `Add to Scene` on the Asset Browser's context menu, which dispatches the `AppUiMsg.RequestAddDrawableFromAsset` the drag-and-drop payload used to. The gesture changed; nothing downstream of the panel can tell.

A frame of the shell needs a text measurer and nothing else, so it can be built headlessly — `tests/RenderLab.Editor.Tests` builds real frames, walks every branch of the Inspector, and drives whole gestures (a click on a row, a context menu, a dialog answered) without a GPU or a hand on the mouse.

The assembly is `RenderLab.Ui.ImGui` but the last namespace segment collides with `ImGuiNET.ImGui` (the ImGui entry-point class) during simple-name lookup. To keep the assembly name matching the folder, each file declares a `using ImGui = ImGuiNET.ImGui;` alias *inside* the namespace — compilation-unit aliases lose to the parent-namespace walk-up, so the alias must live after `namespace RenderLab.Ui.ImGui;`.

## Purity Boundary

Everything in `RenderLab.Graph`, `RenderLab.Scene`, and `RenderLab.Assets` is pure — no side effects, no mutation, fully unit-testable without a GPU.

Everything in `RenderLab.Gpu` and `RenderLab.Platform.Desktop` performs side effects. `GpuState` is the single mutable kernel, passed explicitly by reference — never global, never static. `DeviceCapabilities` is an immutable record on `GpuState`, queried once at device creation — papers read it instead of calling Vulkan directly.

GPU memory flows through a single engine-owned surface: `Allocator` (`Gpu/Allocator.cs`), hung off `GpuState.Allocator`. Every `vkAllocateMemory` goes through it; resource creation returns `(handle, Allocation)` so buffer/memory lifetimes are coupled at the type level, and callers pick a `MemoryIntent` (`GpuOnly`, `CpuToGpu`) instead of hand-rolling memory-property flags. The ImGui per-frame vertex/index buffers grow in doubling steps and stay mapped for the lifetime of the instance, so `vkAllocateMemory` fires O(log N) times at warm-up rather than every resize. Sub-allocation stays on the roadmap for when it becomes a measurable bottleneck.

### Asset boundary

Pure code references meshes, textures, and materials by typed ID — `MeshId` / `TextureId` / `MaterialId` in `RenderLab.Assets`. The pure `IAssetCatalog` view returns CPU-side `MeshAsset` / `TextureAsset` / `MaterialAsset` records and is what scene builders, papers, and panels consume. The shell-side `IGpuAssetResolver` (in `RenderLab.Gpu.Assets`) hands out live `GpuMeshHandles` and `GpuTextureHandles` at the moment of recording, so GPU handles never leak into `Scene`, `Graph`, or push-constant builders. `AssetRegistry` in `RenderLab.Gpu.Assets` is the single owner of both views — it implements `IAssetCatalog` + `IGpuAssetResolver` + `IDisposable` and is constructed with `GpuState` so it can upload, free buffers, and serve in-place edits to material assets via `UpdateMaterial`. Built-in fallbacks (1×1 white texture, default Blinn-Phong material) cover `TextureId.None` and `MaterialId.None` so render code dereferences unconditionally.

Material assets are mutable in place — the editor edits the named asset by id rather than carrying a copy of the parameters on each `Drawable`. The pure UI reducer treats `UiMsg.UpdateMaterialAsset` as a no-op; the shell intercepts those messages and applies them to the registry before resolving the next frame.

`Program.cs` (desktop) is a thirty-line composition root: it builds a `PipelineRegistry` (one factory per pipeline id), picks a project path from argv, and hands both to `Application.Run`. The `Application` owns the window, `GpuState`, `AssetRegistry`, ImGui shell, and the editor's `UiModel` when the active pipeline declares `ConsumesScenes`. Each runnable workspace lives on disk as a project — see [`PROJECT-MODEL.md`](PROJECT-MODEL.md).

### Swapchain present mode (lab policy)

`VulkanSwapchain` deliberately picks `FIFO_RELAXED` over `MAILBOX` and uses exactly `capabilities.MinImageCount` (no `+1`). The AAA default — Mailbox plus an extra image — *hides* frame-time overruns by buffering ahead and replacing un-presented frames; in a research lab that signal is what we want to see. Under this configuration a frame that misses the 16.67 ms budget tears on that frame instead of being smoothed over, and the negotiated image count is logged at startup. `FIFO` (universally supported) is the fallback. See `blogs/ideas/field-notes/swapchain-present-mode/draft.md` for the full reasoning. Note: this couples CPU and GPU more tightly than a pipelined `+1` setup, so technique cost should be measured with GPU timestamp queries (`GpuTimestamps`), not wall-clock frame time.

## Per-Frame Data Flow

```
UiModel (editable per-frame state)
  |
  v
Scene snapshot ..................................... PURE
  Immutable record built each frame in the demo:
  Scene(Camera, ImmutableArray<Drawable>, ImmutableArray<Light>)
  Drawable = (Name, MeshId, Transform, MaterialId). GPU buffers
  are resolved from MeshId at record time; the material asset is
  resolved from MaterialId via IAssetCatalog. Neither lives on Scene.
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
ImGui overlay       -> Application records on top (outside the pipeline's render graph)
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
| `Scene` / `Drawable` | `Scene/Scene.cs`, `Scene/Drawable.cs` | Per-frame immutable snapshot (camera, drawables, lights). `Drawable` carries `(Name, MeshId, Transform, MaterialId)`; pass recorders loop drawables, resolve GPU handles via `IGpuAssetResolver` and material asset via `IAssetCatalog` |
| `MeshId` / `TextureId` / `MaterialId` | `Assets/*.cs` | Opaque typed handles (pure); `None` is the zero sentinel for each |
| `MeshAsset` / `TextureAsset` | `Assets/*.cs` | CPU-side asset records returned by the catalog |
| `MaterialAsset` / `BlinnPhongMaterial` | `Assets/MaterialAsset.cs` | DU root + Blinn-Phong concrete (Albedo, SpecularStrength, Shininess, AlbedoMap). Editable in place via `AssetRegistry.UpdateMaterial` |
| `IAssetCatalog` | `Assets/IAssetCatalog.cs` | Pure read interface from IDs to CPU-side asset records (meshes, textures, materials) |
| `AssetError` | `Assets/AssetError.cs` | DU for asset failures: `FileNotFound`, `InvalidFormat`, `GpuUploadFailed`, `UnknownId` |
| `AssetRegistry` | `Gpu/Assets/AssetRegistry.cs` | Shell owner of mesh + texture GPU resources and material asset records; implements `IAssetCatalog` + `IGpuAssetResolver`; built-in white texture + default material registered at construction |
| `IGpuAssetResolver` / `GpuMeshHandles` / `GpuTextureHandles` | `Gpu/Assets/` | Shell-side resolver from IDs to live vertex/index buffers and image view + sampler |
| `MaterialDescriptors` | `Gpu/Assets/MaterialDescriptors.cs` | Per-`TextureId` descriptor-set cache for the GBuffer material slot; one set per texture, reused across frames |
| `GltfLoader` | `Assets/GltfLoader.cs` | Pure parser (SharpGLTF + StbImageSharp) producing a `GltfImport` blueprint: meshes, RGBA textures, materials (PBR baseColor → Blinn-Phong), drawable seeds with raw position/scale and index cross-refs |
| `AssetRegistry.ImportGltf` | `Gpu/Assets/AssetRegistry.cs` | Shell orchestration of a `GltfImport`: registers in dependency order (textures → materials → meshes → drawables) and rewrites indices into real ids; returns `GltfImportResult` for the Application to dispatch as `UiMsg.AddDrawable` |
| `ProjectManifest` / `SceneDocument` | `Project/*.cs` | Pure on-disk model — `project.json` + `*.scene.json` schemas with `System.Text.Json` polymorphism for lights, materials, asset sources |
| `ProjectIO` | `Project/ProjectIO.cs` | JSON read/write for manifests + scenes; path-sandbox normalisation that rejects `..` escapes |
| `IProceduralAssetSource` / `DefaultProceduralAssets` | `Assets/*.cs` | Pure registry of named procedural generators (`sphere` / `cube` / `checker`) used by `SceneLoader` to materialise `procedural` asset sources without baking pixels into scene files |
| `IPipeline` | `Pipelines/IPipeline.cs` | The runtime contract for a rendering technique: `Initialize` / `RecreateTransient` / `RecordFrame`, `ConsumesScenes` flag, optional `DrawDebugUi` + `GetFrameStats` for editor integration, optional `ReloadShaders` for hot reload |
| `ShaderHotReload` | `Gpu/ShaderHotReload.cs` | Debug-only file watcher over `src/RenderLab.Shaders/` that shells out to `glslc` on change and invokes the active pipeline's `ReloadShaders`. F5 force-reloads everything. Silently disables itself when `glslc` is missing or the source tree is unreachable (published build). See [`HOT-SHADER-RELOAD.md`](HOT-SHADER-RELOAD.md) |
| `PipelineRegistry` | `Pipelines/PipelineRegistry.cs` | Maps the `pipeline` string in `project.json` to a factory (`Resolve` returns a `Result`) |
| `SceneLoader` / `SceneAssetResolver` / `SceneBuilder` | `Pipelines/*.cs` | `SceneLoader` turns a `SceneDocument` into a runtime `UiModel` by walking each drawable's `AssetRef` through `SceneAssetResolver`, which lazily registers previously-unseen refs into `AssetRegistry` and caches the id across scene swaps. `SceneBuilder` is the pure projection from `UiModel` + aspect → immutable `Scene` snapshot |
| `Application` | `App/Application.cs` | Single composition root replacing the per-demo bootstraps. Hosts window/GPU/ImGui/AssetRegistry/UiModel; drives the frame loop; routes registry-side asset edits (import, remove) for the deferred pipeline |

## Build and Run

```bash
# Prerequisites: .NET 9 SDK, Vulkan SDK (for glslc)

# Desktop — runs the default project (deferred)
dotnet build code.sln
dotnet run --project src/RenderLab.App

# Desktop — pick a specific project (see docs/PROJECT-MODEL.md)
dotnet run --project src/RenderLab.App -- code/projects/triangle   # Post 2
dotnet run --project src/RenderLab.App -- code/projects/gbuffer    # Post 3
dotnet run --project src/RenderLab.App -- code/projects/deferred   # Post 4

# Or any folder containing a project.json
dotnet run --project src/RenderLab.App -- C:\path\to\my-project

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
  RenderLab.Assets/            MeshId, MeshAsset, Vertex3D, MeshData, ObjLoader,
                               IAssetCatalog, AssetError (pure data + parsing)
  RenderLab.Gpu/               Vulkan device, swapchain, buffers, images,
                               pipelines, descriptors, graph executor,
                               Allocator, DeviceCapabilities, PushConstants,
                               Assets/AssetRegistry (mesh registry + GPU resolver)
  RenderLab.Scene/             Scene snapshot, Camera, Drawable, Light DU
                               (PointLight, DirectionalLight), Direction,
                               Intensity, HemisphericAmbient, MaterialParams,
                               FreeCameraController
  RenderLab.Platform.Desktop/  GLFW window wrapper (poll-based)
  RenderLab.Papers/            Pass modules: GBufferPass, DeferredLighting,
                               TonemapPass, DebugVizPass
  RenderLab.Ui/                Pure Elm-style UI state (Model/Msg/Update/Intent)
  RenderLab.Ui.ImGui/          Imperative shell for RenderLab.Ui: ImGui views + GPU timestamps
  RenderLab.Editor/            Ptah view layer replacing Ui.ImGui (shell layout + ported panels)
  RenderLab.Project/           Pure project + scene document model + JSON IO;
                               project asset index + scanner (Project panel)
  RenderLab.Pipelines/         IPipeline + Triangle/GBuffer/Deferred + SceneLoader + SceneBuilder
  RenderLab.Shaders/           GLSL sources + SPIR-V build script
  RenderLab.App/               Application composition root + Program.cs (project-path argv)
projects/
  triangle/, gbuffer/, deferred/   Starter projects (project.json + assets/ + scenes/)
tests/
  RenderLab.Functional.Tests/  Optional, Result, Pipe
  RenderLab.Graph.Tests/       Topological sort, barrier insertion, cycle detection
  RenderLab.Scene.Tests/       Camera math, free-fly controller, material packing
  RenderLab.Ui.Tests/          Pure UI reducers (Model/Msg/Update)
  RenderLab.Project.Tests/     Manifest + scene round-trip, path-sandbox, index validation
```
