# Project Model

A **project** is the runnable unit of the lab. It is a folder on disk with a `project.json` manifest, a mandatory `assets/` subfolder, and zero-or-more `*.scene.json` files. The engine takes a project path as its single argument:

```
dotnet run --project src/RenderLab.App -- code/projects/deferred
```

The starter projects under `code/projects/` (`triangle/`, `gbuffer/`, `deferred/`) replaced the per-article demo classes that used to live in `code/src/RenderLab.App/Demos/`. There is no demo dispatcher and no `IDemo` interface — the engine resolves the project's pipeline id against a `PipelineRegistry`, opens the default scene if the pipeline declares `ConsumesScenes`, and runs.

## On-disk layout

```
my-project/
  project.json                      ← manifest (text)
  assets/
    box-textured.glb                ← vendored binary content
  scenes/
    main.scene.json                 ← default scene
    materials-test.scene.json       ← additional scenes (optional)
```

`project.json`:

```json
{
  "version": 1,
  "name": "Deferred Lab",
  "pipeline": "deferred",
  "defaultScene": "scenes/main.scene.json",
  "scenes": ["scenes/main.scene.json", "scenes/materials-test.scene.json"]
}
```

`pipeline` is the string id registered in `PipelineRegistry` (`triangle`, `gbuffer`, `deferred`). `defaultScene` and `scenes` entries are project-relative paths; `ProjectIO.ResolveProjectPath` rejects anything that escapes the project root after normalisation.

A scene file holds camera, ambient, lights, render config, asset declarations (meshes / textures / materials), and drawables. Assets are scene-scoped; drawables reference them by index into the per-scene arrays. See `code/projects/deferred/scenes/main.scene.json` for a worked example. The full schema lives in `code/src/RenderLab.Project/SceneDocument.cs`.

## Pipeline contract

`IPipeline` (in `RenderLab.Pipelines`) is the single contract a rendering technique implements:

- `Id` — must match the `pipeline` field in `project.json`.
- `ConsumesScenes` — when `true`, the Application opens the default scene, builds a `Scene` snapshot each frame from `UiModel`, and shows the editor panels (Scene, AssetBrowser, etc.) via `UiView.Draw`.
- `Initialize(gpu, assets, overlayRenderPass)` — long-lived GPU resources.
- `RecreateTransient(gpu)` — swapchain-sized resources.
- `RecordFrame(gpu, cb, scene, ui, dt, imageIndex)` — record draw commands; must leave the swapchain image in `PresentSrcKhr`.
- `DrawDebugUi()` — optional pipeline-specific ImGui windows (e.g. GBuffer's vizMode combo).
- `GetFrameStats(dt)` — optional snapshot of GPU timestamps + the resolved render graph for the editor's debug panels.

The Application records the ImGui overlay pass on top of whatever the pipeline left in the swapchain — pipelines no longer need to know about the overlay render pass beyond receiving it in `Initialize` (some pipelines may want to chain their own LoadOp.Load passes).

## Save / load split

The pure layer (`RenderLab.Project`) reads and writes documents — bytes ↔ records — without any GPU calls. `SceneLoader` (in `RenderLab.Pipelines`) is the impure boundary: it walks the document, calls `AssetRegistry.RegisterMesh / RegisterTexture / RegisterMaterial` (resolving procedural sources via `IProceduralAssetSource`), and returns a `LoadedScene(UiModel, SceneAssetSources)`. The save side is the inverse — `SceneDocumentBuilder.From(ui, catalog, sources)` is pure.

`SceneAssetSources` is the runtime mapping from registered ids back to the symbolic `AssetSourceDoc` they came from. The loader populates it as it materialises the scene; the shell extends it on every glTF import; the builder consumes it on save so file paths and procedural generator parameters round-trip into the on-disk scene without ever baking pixels or vertices.

This mirrors the existing `IAssetCatalog` / `IGpuAssetResolver` split: the document model is pure data; the registry is the single owner of GPU lifetimes.

## Save / multi-scene UX

The editor's `File` menu drives the project / scene lifecycle:

- **Save Scene** (`Ctrl+S`) — writes the active scene to its `*.scene.json` via `SceneDocumentBuilder` + `ProjectIO.WriteScene`. The menu label includes the scene path and a `*` when `AppUiModel.SceneDirty` is set.
- **Save Scene As…** — opens a `*.scene.json` save dialog, defaults to the project's `scenes/` folder, and adds the new scene to the manifest's `scenes` list (so it shows up in *Open Scene* immediately).
- **Open Scene** — submenu listing every scene in `manifest.Scenes`. Clicking switches by waiting for GPU idle, calling `IPipeline.ResetSceneState()` (drops scene-keyed descriptor caches), `AssetRegistry.ResetForSceneSwap()` (releases non-builtin meshes/textures/materials), then re-running `SceneLoader.Load` against the new document.
- **Reload Scene** — re-reads the active scene from disk, dropping any unsaved edits.
- **Open Project…** / **New Project…** — folder picker. *New Project* writes a minimal `project.json` + `assets/` + a sphere starter scene (`deferred` pipeline) and opens it. Switching projects also disposes the current `IPipeline` and instantiates a fresh one resolved from the new manifest's pipeline id.

The `SceneDirty` flag is set on every change to the runtime `UiModel` (camera drag, panel edit, drawable transform) and on every registry-side asset edit (material slider). It clears on save and on scene swap.

## Per-user editor settings

`%LOCALAPPDATA%\RenderLab\editor.json` (Windows) — or the platform equivalent of `Environment.SpecialFolder.LocalApplicationData` — persists `EditorSettings { LastProjectPath, LastScenePath, HiddenPanels[] }`. The schema lives in `RenderLab.Project/EditorSettings.cs`; the IO is `EditorSettingsIO.ReadOrDefault()` / `Write(settings)`.

Restore order on startup:

1. Explicit project path argv → use as-is.
2. Else `LastProjectPath` from settings if the folder still exists.
3. Else the staged `projects/deferred` next to the binary.

If the restored project still lists `LastScenePath` in its `manifest.Scenes`, the Application opens that scene in place of the manifest's `defaultScene`. Settings persist on every scene/project switch and on shutdown — best-effort, so a read-only settings folder never blocks launch.

## Procedural assets

`{ "kind": "procedural", "generator": "sphere", "params": { "stacks": 32, "slices": 32 } }` is a *symbolic* reference — the loader calls `IProceduralAssetSource.TryCreateMesh`/`TryCreateTexture` to materialise it. `DefaultProceduralAssets` ships with `sphere`, `cube`, and `checker`. New generators register an additional `IProceduralAssetSource`. Procedural assets are never baked to PNG/OBJ on save: the generator name + params are the source of truth, so scene diffs stay tiny and tweaks to a generator propagate to every scene that uses it.

## Adding a project

1. Pick a pipeline id (`triangle` / `gbuffer` / `deferred` today; new pipelines add a `Register` line in `Program.cs`).
2. Create `my-project/project.json` with that id.
3. Create `my-project/assets/` (mandatory — `ProjectIO.ReadManifest` rejects projects without it).
4. For scene-consuming pipelines, write at least one `*.scene.json` and point `defaultScene` at it.
5. Run: `dotnet run --project src/RenderLab.App -- path/to/my-project`.

Bundled starter projects copy into `bin/.../projects/` via the App csproj's per-project content glob; the no-arg launch resolves to that staged folder when present.
