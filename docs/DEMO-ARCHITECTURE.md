# Demo Architecture

`RenderLab.App` hosts multiple self-contained demos, one per blog article. Each demo is a class in `Demos/` that implements `IDemo` and owns its full lifecycle — window, GPU, render loop, cleanup.

## Structure

```
RenderLab.App/
  Program.cs                     CLI dispatcher — picks a demo by name
  Demos/
    IDemo.cs                     interface: Run() + IDisposable
    TriangleDemo.cs              Post 2 — minimal pipeline, single pass
    GBufferDemo.cs               Post 3 — geometry pass + buffer visualization
    DeferredDemo.cs              Post 4 — full deferred pipeline with render graph
```

```
dotnet run -- triangle           # Post 2
dotnet run -- gbuffer            # Post 3
dotnet run -- deferred           # Post 4 (default)
```

## Why

The code advances faster than the articles. Once the deferred pipeline was working, there was no way to go back and take screenshots for the earlier "minimal triangle" or "G-Buffer only" articles. Each article tells a distinct story with a specific subset of the engine, and readers should see exactly what the article describes — not the latest state.

## Design Decisions

### One project, multiple demo classes (chosen)

All demos live in `RenderLab.App` and share the same library dependencies (`RenderLab.Gpu`, `RenderLab.Scene`, etc.). `Program.cs` is a thin switch on `args[0]`.

- Single `csproj`, single build output, single set of shader assets.
- Demos share compiled infrastructure but not runtime state — each class owns its own `GpuState`, window, and resources.
- Adding a demo means one new class file and one line in the switch.

### Separate projects per demo (rejected)

A `RenderLab.Demo.Triangle` project, a `RenderLab.Demo.Deferred` project, etc.

- Duplicated `csproj` boilerplate and shader copy targets.
- No shared runtime state to isolate anyway — the demos are already independent classes.
- More friction to add a new demo (new project, new solution entry, new build target).

### Conditional compilation / feature flags (rejected)

`#if DEMO_TRIANGLE` or similar compile-time switching.

- Only one demo available per build. Cannot switch at runtime for quick comparisons.
- Ugly to maintain, easy to break with stale `#if` blocks.

## Each Demo Tells One Article's Story

| Demo | Article | What it shows | What it omits |
|------|---------|---------------|---------------|
| `TriangleDemo` | Post 2 — Minimal Pipeline | Single pass, hardcoded RGB triangle, frame sync | Multi-pass, meshes, ImGui |
| `GBufferDemo` | Post 3 — G-Buffer | Geometry pass, MRT, buffer visualization, manual barriers | Lighting, tonemap, render graph |
| `DeferredDemo` | Post 4 — Lighting + Render Graph | Full pipeline: GBuffer → Lighting → Tonemap, render graph compiler | — |

Each demo is deliberately incomplete relative to the next. `GBufferDemo` has no lighting because the article ends with "the screen is dark." `TriangleDemo` has no mesh loading because the article focuses on the eight concepts needed for a single triangle. The omissions are the point — they match the narrative.

## Adding a New Demo

1. Create `Demos/SomethingDemo.cs` implementing `IDemo`.
2. Add one case to the switch in `Program.cs`.
3. The demo owns its full lifecycle — init, loop, cleanup — and shares nothing with other demos at runtime.
