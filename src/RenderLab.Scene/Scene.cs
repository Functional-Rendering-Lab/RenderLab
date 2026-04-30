using System.Collections.Immutable;

namespace RenderLab.Scene;

/// <summary>
/// One drawable in the scene: geometry, world placement, and Blinn-Phong
/// material. <see cref="Name"/> is for inspectors and future selection — it
/// has no semantic meaning to the renderer.
/// </summary>
public sealed record SceneMesh(
    string Name,
    MeshData Data,
    Transform Transform,
    MaterialParams Material);

/// <summary>
/// Immutable snapshot of everything the renderer needs to know about the world
/// for a single frame: camera, drawable meshes, and lights. Built per-frame
/// from the editable UI state and consumed by pass recorders. Render-config
/// (shading mode, visualization mode, clear color) is intentionally not part
/// of the scene — it lives on <c>UiModel</c>.
/// </summary>
public sealed record Scene(
    Camera Camera,
    ImmutableArray<SceneMesh> Meshes,
    ImmutableArray<Light> Lights);
