# Documentation Rules

How and where we document RenderLab. Two channels: **markdown files** for decisions, **XML comments** for API.

---

## 1. Decision Documents (`docs/*.md`)

Every architectural decision, platform port, failed approach, and design trade-off is recorded as a markdown file in `docs/`.

| Document | Purpose |
|----------|---------|
| `ARCHITECTURE.md` | Module graph, purity boundary, data flow, key abstractions, build commands |
| `ARCHITECTURE-ANDROID.md` | Android porting journey: what was tried, what failed, why Mono, NativeAOT path forward |
| `ADDING-A-PAPER.md` | How to add a new paper implementation |
| `DEMO-ARCHITECTURE.md` | Why and how `RenderLab.App/Demos/` hosts one demo per blog article |
| `DOCUMENTATION-RULES.md` | This file |

### What gets a document

- A new platform port or runtime change
- A design decision with rejected alternatives (record the alternatives and why they lost)
- A multi-phase migration (record each phase as it completes)
- Anything a future contributor would otherwise have to reverse-engineer from git history

### What does NOT get a document

- Bug fixes (the commit message is enough)
- Routine feature additions that follow existing patterns
- Anything derivable by reading the current code

### Format

- Lead with current state, not history. A reader should get the answer in the first paragraph.
- Use tables for comparisons (desktop vs Android, option A vs option B).
- Code blocks for architecture diagrams (ASCII art, not images).
- Keep documents updated when the state they describe changes. Stale docs are worse than no docs.

---

## 2. Code Documentation (XML `<summary>`)

Follow the **80/20 rule**: document the 20% of public API surface that carries 80% of the understanding. Not every public member needs a doc comment.

### Must document

- **Type declarations** (`class`, `record`, `struct`, `enum`) — one sentence explaining what it is and where it fits.
- **Non-obvious public methods** — methods where the name alone doesn't convey the contract, side effects, or Vulkan semantics.
- **Record parameters with domain meaning** — use `<param>` tags on positional records when the field name alone is ambiguous (e.g. `ApiVersion` could mean requested or actual).
- **Enum members mapping to Vulkan concepts** — brief note on the corresponding `VK_IMAGE_LAYOUT_*` or stage/access flags.

### Do NOT document

- Self-explanatory methods (`Dispose`, `ToString`, trivial property accessors).
- Private/internal implementation details (these change freely).
- Method bodies — if logic needs explaining, refactor or add a single inline comment at the non-obvious line.
- Wrapper overloads — use `<inheritdoc>` to point at the primary overload.

### Style

- `<summary>` is one sentence, rarely two. Start with a verb or noun, not "This method..." or "Gets or sets...".
- `<param>` tags only when the parameter name isn't self-documenting.
- `<see cref="..."/>` to link related types, especially across module boundaries.
- `<returns>` only when non-obvious (not needed for `Create` methods returning what the name says).
- No `<remarks>` essays. If it takes a paragraph to explain, it belongs in a `docs/*.md` file, not inline.

### Examples

Good:
```csharp
/// <summary>
/// Immutable snapshot of physical device properties and features, queried once
/// during <see cref="VulkanDevice.Create"/>.
/// </summary>
public sealed record DeviceCapabilities( ... );
```

Good (non-obvious contract):
```csharp
/// <summary>
/// Finds the best supported depth format. Prefers D32_SFLOAT, falls back to
/// D24_UNORM_S8_UINT (common on Android/mobile GPUs), then D16_UNORM.
/// </summary>
public static Format FindDepthFormat(Vk vk, PhysicalDevice physicalDevice)
```

Unnecessary (name says it all):
```csharp
/// <summary>Destroys the framebuffers.</summary>  // don't write this
public static void DestroyFramebuffers(GpuState state, Framebuffer[] framebuffers)
```

---

## 3. Keeping It Current

- When you change architecture, update `ARCHITECTURE.md` in the same PR.
- When you complete a migration phase, update the relevant `ARCHITECTURE-*.md`.
- When you add a new public type to `RenderLab.Gpu` or `RenderLab.Graph`, add the `<summary>`.
- When a doc comment becomes wrong, fix or delete it. Wrong docs are worse than missing docs.
