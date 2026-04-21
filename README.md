# Render Lab

Vulkan rendering engine in C#/.NET 9, built as a public learning journey. Each demo in this repo corresponds to a blog post walking through the technique.

Website, blog posts, and articles: **https://functionalrenderinglab.dev**

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- A Vulkan-capable GPU and up-to-date drivers
- [Vulkan SDK](https://vulkan.lunarg.com/) (for validation layers and shader tooling during development)

## Build

```bash
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

Running with no argument launches the deferred demo.

## License

Apache License 2.0 — see [LICENSE](LICENSE).
