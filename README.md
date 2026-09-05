# Render Lab

A Vulkan rendering engine in C#/.NET 9, built as a public learning journey.
Each starter project in this repo corresponds to a blog post on [functionalrenderinglab.dev](https://functionalrenderinglab.dev) that walks through the technique it implements.

The architecture follows **Functional Core / Imperative Shell**: the render-graph compiler, scene types, and UI reducers are pure and unit-tested without a GPU; all Vulkan calls are confined to a single mutable kernel (`GpuState`) behind a thin shell.

## Status

The engine is mid-roadmap.
M0 to M3 (triangle → G-Buffer → deferred shading) are complete.
M5 (lab as editor) and M6 (project as the runnable unit) have landed, and so has M7 (shader hot-reload).
M4 (basic lighting) is the one still open: Blinn-Phong and multiple point lights are in, directional plus hemispheric ambient is next.
See [`RenderLab-PRD.md`](RenderLab-PRD.md) for the full milestone plan and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the module graph and per-frame data flow.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- A Vulkan-capable GPU and up-to-date drivers
- [Vulkan SDK](https://vulkan.lunarg.com/), which provides `glslc` for shader compilation and the validation layers
- Python 3, invoked by `compile_shaders.py` to drive `glslc`

Targeted platforms: Windows and Linux on Vulkan 1.3.

## Build

```bash
# Compile GLSL shaders to SPIR-V (run once, and after any shader edit)
python src/RenderLab.Shaders/compile_shaders.py

# Build the solution
dotnet build code.sln
```

## Run a project

A **project** is the runnable unit: a folder with a `project.json` manifest, an `assets/` subfolder, and zero or more `*.scene.json` files.
`RenderLab.App` takes a project path as its single argument:

```bash
# from code/
dotnet run --project src/RenderLab.App -- <path-to-project>
```

| Starter project | Command | What it shows |
|------|---------|---------------|
| Triangle | `dotnet run --project src/RenderLab.App -- projects/triangle` | Minimal Vulkan pipeline, the "hello world" of the engine |
| G-Buffer | `dotnet run --project src/RenderLab.App -- projects/gbuffer` | Geometry pass writing position, normal, and albedo targets |
| Deferred | `dotnet run --project src/RenderLab.App -- projects/deferred` | Full deferred shading pipeline with Blinn-Phong lighting |

Running with no argument reopens the last project from the per-user editor settings, and falls back to the staged `deferred` project when there is no such setting.
The project model, the `IPipeline` contract, and the save / multi-scene flow are described in [`docs/PROJECT-MODEL.md`](docs/PROJECT-MODEL.md).

## Run tests

The pure modules are tested without a GPU:

```bash
dotnet test code.sln
```

Five test projects cover them: `RenderLab.Graph.Tests` (graph compilation and barriers), `RenderLab.Scene.Tests` (camera math and material packing), `RenderLab.Ui.Tests` (the Elm-style reducers), `RenderLab.Project.Tests` (manifest and scene round-trips), and `RenderLab.Editor.Tests` (panel layout and frame building).

## Repository layout

- `src/RenderLab.App/`: `Application` composition root plus `Program.cs`, which resolves the project path
- `src/RenderLab.Gpu/`: the only assembly that calls Silk.NET Vulkan
- `src/RenderLab.Graph/`, `RenderLab.Scene/`, `RenderLab.Ui/`, `RenderLab.Functional/`: pure modules
- `src/RenderLab.Project/`: pure project + scene document model and its JSON IO
- `src/RenderLab.Pipelines/`: the `IPipeline` contract, the triangle / G-Buffer / deferred pipelines, and scene loading
- `src/RenderLab.Papers/`: pass modules (G-Buffer, deferred lighting, tonemap, debug viz)
- `src/RenderLab.Editor/`: the editor view layer, built on the Ptah immediate-mode UI
- `src/RenderLab.Assets/`: CPU-side asset records and loaders (OBJ, glTF, textures, materials)
- `src/RenderLab.Shaders/`: GLSL sources + SPIR-V build script
- `src/RenderLab.Platform.Desktop/`: GLFW window and input via Silk.NET
- `projects/`: the `triangle`, `gbuffer`, and `deferred` starter projects
- `docs/`: architecture, documentation rules, project model, paper-implementation guide
- `tests/`: unit tests for the pure modules

## Further reading

- [`RenderLab-PRD.md`](RenderLab-PRD.md): goals, non-goals, milestones, design decisions
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md): module graph, purity boundary, data flow
- [`docs/PROJECT-MODEL.md`](docs/PROJECT-MODEL.md): projects, the `IPipeline` contract, save and multi-scene UX
- [`docs/ADDING-A-PAPER.md`](docs/ADDING-A-PAPER.md): how to implement a paper against the engine
- [`docs/HOT-SHADER-RELOAD.md`](docs/HOT-SHADER-RELOAD.md): how shader hot reload works and what it covers
- [`docs/QOL-STRATEGY.md`](docs/QOL-STRATEGY.md): debug tooling and the iteration loop
- [`docs/DOCUMENTATION-RULES.md`](docs/DOCUMENTATION-RULES.md): conventions for `docs/*.md` and XML `<summary>`

## License

Apache License 2.0.
See [LICENSE](LICENSE).
