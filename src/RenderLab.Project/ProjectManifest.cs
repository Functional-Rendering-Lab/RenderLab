namespace RenderLab.Project;

/// <summary>
/// Top-level project manifest, persisted as <c>project.json</c> at the root of
/// a project folder. Pure data — no Vulkan, no I/O. The shell-side
/// <see cref="ProjectIO"/> reads and writes instances; <see cref="Pipeline"/>
/// is a string id resolved against the engine's pipeline registry at load time.
/// </summary>
/// <param name="Version">Schema version. Reserved for forward-compatibility — only <c>1</c> is recognised today.</param>
/// <param name="Name">Display name (used for the window title).</param>
/// <param name="Pipeline">Pipeline id, e.g. <c>"triangle"</c> / <c>"gbuffer"</c> / <c>"deferred"</c>.</param>
/// <param name="DefaultScene">Project-relative path to the scene that opens on launch. Empty string for pipelines that don't consume a scene.</param>
/// <param name="Scenes">Project-relative paths of every scene the editor should list. May be empty.</param>
public sealed record ProjectManifest(
    int Version,
    string Name,
    string Pipeline,
    string DefaultScene,
    string[] Scenes);
