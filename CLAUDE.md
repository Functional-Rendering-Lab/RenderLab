# Code Pillar — RenderLab Engine

Scope: this file applies to any work under `code/`. For blog or web-page work, stop and route back to the root `CLAUDE.md`.

## Always read first

Before making any non-trivial change to the engine, read:

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — module graph, purity boundary, per-frame data flow, key abstractions, build commands.
- [`docs/DOCUMENTATION-RULES.md`](docs/DOCUMENTATION-RULES.md) — when to write `docs/*.md` vs XML `<summary>` comments, and the 80/20 rule for API docs.

When planning a new feature or milestone, also read [`RenderLab-PRD.md`](RenderLab-PRD.md) for goals and milestone context.

## When something is unclear, check `docs/` first

If a doc exists for the area you are touching, read it before proposing changes. Available decision records:

| Doc | When to read |
|---|---|
| `docs/ARCHITECTURE.md` | Any structural change, new module, new abstraction |
| `docs/ADDING-A-PAPER.md` | Adding a new paper implementation |
| `docs/DEMO-ARCHITECTURE.md` | Why and how `RenderLab.App/Demos/` hosts one demo per article |
| `docs/QOL-STRATEGY.md` | Tooling, developer experience, debug workflows |
| `docs/DOCUMENTATION-RULES.md` | Writing or editing any doc or XML comment |

If you cannot find an answer in `docs/`, read the relevant code — do not guess.

## Folder map

```
code/
  code.sln
  RenderLab-PRD.md              product requirements, milestones
  docs/                         architectural decision records
  src/
    RenderLab.App               desktop composition root (wires everything)
    RenderLab.Gpu               Vulkan bindings, GpuState (impure kernel)
    RenderLab.Graph             pure render graph compiler
    RenderLab.Scene             immutable scene data (Camera, Mesh, Vertex, PointLight, MaterialParams)
    RenderLab.Papers            paper implementations (DeferredLighting)
    RenderLab.Functional        Optional, Result, Pipe
    RenderLab.Ui                pure Elm-style UI state (Model/Msg/Update/Intent)
    RenderLab.Ui.ImGui          imperative shell for RenderLab.Ui: ImGui views + GPU timestamps
    RenderLab.Shaders           GLSL / SPIR-V shaders
    RenderLab.Platform.Desktop  GLFW window
  tests/
    RenderLab.Functional.Tests
    RenderLab.Graph.Tests
```

## Tooling

**Use Serena MCP for all code reading and writing.** Prefer Serena's semantic tools (`get_symbols_overview`, `find_symbol`, `read_file`, `replace_symbol_body`, `insert_after_symbol`, etc.) over raw file reads and text edits. This gives symbol-aware navigation and safer refactoring. Fall back to standard tools only when Serena cannot handle the operation (e.g. non-code files, shell commands).

## Core rules

1. **Functional Core / Imperative Shell.** `RenderLab.Graph`, `RenderLab.Scene`, and `RenderLab.Functional` are pure and have zero internal dependencies. Keep it that way — no Vulkan types, no mutation, no I/O.
2. **Single mutable kernel.** `GpuState` is the only mutable state. Pass it by reference; never make it global or static.
3. **Tests without a GPU.** New pure logic gets a test under `tests/`. If a change cannot be unit-tested without a GPU, it probably belongs behind the purity boundary.
4. **Follow `DOCUMENTATION-RULES.md`.** Decisions go in `docs/*.md`, API contracts go in XML `<summary>`. Do not invent a third channel.
5. **Update docs in the same change.** If you change architecture, update `docs/ARCHITECTURE.md` alongside the code.
