# Render Lab

A Vulkan rendering engine in C#/.NET 9, built as a public learning journey. Each
demo in this repo corresponds to a blog post on
[functionalrenderinglab.dev](https://functionalrenderinglab.dev) that walks through
the technique it implements.

The architecture follows **Functional Core / Imperative Shell**: the render-graph
compiler, scene types, and UI reducers are pure and unit-tested without a GPU;
all Vulkan calls are confined to a single mutable kernel (`GpuState`) behind a
thin shell.

## Status

The engine is mid-roadmap. M0–M3 (triangle → G-Buffer → deferred shading) are
complete; M4 (basic lighting) and M5 (first paper: SSAO) are in progress. See
[`RenderLab-PRD.md`](RenderLab-PRD.md) for the full milestone plan and
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the module graph and per-frame
data flow.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- A Vulkan-capable GPU and up-to-date drivers
- [Vulkan SDK](https://vulkan.lunarg.com/) — provides `glslc` for shader compilation and the validation layers
- Python 3 — invoked by `compile_shaders.py` to drive `glslc`

Targeted platforms: Windows and Linux on Vulkan 1.3.

## Build

```bash
# Compile GLSL shaders to SPIR-V (run once, and after any shader edit)
python src/RenderLab.Shaders/compile_shaders.py

# Build the solution
dotnet build code.sln
```

## Run a demo

Demos are selected by CLI argument to `RenderLab.App`:

```bash
dotnet run --project src/RenderLab.App -- <demo>
```

| Demo | Command | What it shows |
|------|---------|---------------|
| Triangle | `dotnet run --project src/RenderLab.App -- triangle` | Minimal Vulkan pipeline — the "hello world" of the engine |
| G-Buffer | `dotnet run --project src/RenderLab.App -- gbuffer` | Geometry pass writing position, normal, and albedo targets |
| Deferred | `dotnet run --project src/RenderLab.App -- deferred` | Full deferred shading pipeline with Blinn-Phong lighting (default) |

Running with no argument launches the deferred demo. The rationale for the
per-article demo split is in
[`docs/DEMO-ARCHITECTURE.md`](docs/DEMO-ARCHITECTURE.md).

## Run tests

The pure modules (`RenderLab.Graph`, `RenderLab.Scene`, `RenderLab.Ui`,
`RenderLab.Functional`) are fully tested without a GPU:

```bash
dotnet test code.sln
```

## Repository layout

- `src/RenderLab.App/` — CLI dispatcher and per-article demo classes (`Demos/`)
- `src/RenderLab.Gpu/` — the only assembly that calls Silk.NET Vulkan
- `src/RenderLab.Graph/`, `RenderLab.Scene/`, `RenderLab.Ui/`, `RenderLab.Functional/` — pure modules
- `src/RenderLab.Papers/` — pass modules (G-Buffer, deferred lighting, tonemap, debug viz)
- `src/RenderLab.Shaders/` — GLSL sources + SPIR-V build script
- `docs/` — architecture, documentation rules, paper-implementation guide
- `tests/` — unit tests for the pure modules

## Further reading

- [`RenderLab-PRD.md`](RenderLab-PRD.md) — goals, non-goals, milestones, design decisions
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — module graph, purity boundary, data flow
- [`docs/ADDING-A-PAPER.md`](docs/ADDING-A-PAPER.md) — how to implement a paper against the engine
- [`docs/DEMO-ARCHITECTURE.md`](docs/DEMO-ARCHITECTURE.md) — why each blog post gets its own demo class
- [`docs/QOL-STRATEGY.md`](docs/QOL-STRATEGY.md) — debug tooling and the iteration loop
- [`docs/DOCUMENTATION-RULES.md`](docs/DOCUMENTATION-RULES.md) — conventions for `docs/*.md` and XML `<summary>`

## License

Apache License 2.0 — see [LICENSE](LICENSE).
