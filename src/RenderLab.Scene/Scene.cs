using System.Collections.Immutable;

namespace RenderLab.Scene;

/// <summary>
/// Immutable snapshot of everything the renderer needs to know about the world
/// for a single frame: camera, drawables, and lights. Built per-frame from the
/// editable UI state and consumed by pass recorders. Render-config (shading
/// mode, visualization mode, clear color) is intentionally not part of the
/// scene — it lives on <c>UiModel</c>.
/// </summary>
public sealed record Scene(
    Camera Camera,
    ImmutableArray<Drawable> Drawables,
    ImmutableArray<Light> Lights);
