namespace RenderLab.Ui;

/// <summary>
/// Shell-scope messages emitted by the menu bar and asset browser:
/// panel toggles, exit, registry-side asset operations, and project /
/// scene lifecycle requests the shell performs on behalf of the pure
/// layer.
/// </summary>
public abstract record AppUiMsg
{
    public sealed record TogglePanel(PanelId Id) : AppUiMsg;
    public sealed record SetPanelVisible(PanelId Id, bool Visible) : AppUiMsg;
    public sealed record RequestExit : AppUiMsg;
    /// <summary>One-shot import trigger (registry side-effect). The reducer
    /// passes through; the shell calls <c>AssetRegistry.ImportGltf</c> and
    /// dispatches <c>UiMsg.AddDrawable</c> for each imported drawable.</summary>
    public sealed record RequestImportGltf(string Path) : AppUiMsg;
    /// <summary>Open the OS file picker, then import the chosen file.</summary>
    public sealed record RequestImportGltfDialog : AppUiMsg;
    /// <summary>Remove a registered asset. The shell validates reference
    /// safety, calls the registry, and invalidates any caches keyed on the id.</summary>
    public sealed record RequestRemoveMesh(RenderLab.Assets.MeshId Id) : AppUiMsg;
    public sealed record RequestRemoveTexture(RenderLab.Assets.TextureId Id) : AppUiMsg;
    public sealed record RequestRemoveMaterial(RenderLab.Assets.MaterialId Id) : AppUiMsg;

    /// <summary>
    /// Persist edited Blinn-Phong material parameters back to the
    /// <c>.mat.json</c> sidecar identified by <paramref name="Guid"/>, then
    /// — if the material is currently registered — apply the same change to
    /// the runtime registry for live preview. Parameters travel as primitives
    /// to keep <c>RenderLab.Ui</c> free of a <c>RenderLab.Project</c>
    /// dependency.
    /// </summary>
    public sealed record RequestUpdateMaterial(
        System.Guid Guid,
        float[] Albedo,
        float SpecularStrength,
        float Shininess,
        System.Guid? AlbedoTexGuid,
        string? AlbedoTexSub) : AppUiMsg;

    // ─── Project / scene lifecycle (M6.2 + M6.3) ───────────────────────

    /// <summary>Save the current scene back to <c>activeScenePath</c>.</summary>
    public sealed record RequestSaveScene : AppUiMsg;
    /// <summary>Save-As dialog → write the current scene to the chosen path.
    /// Path may be absolute (will be converted to project-relative).</summary>
    public sealed record RequestSaveSceneAs : AppUiMsg;
    /// <summary>Open a folder picker, then load the chosen project's default scene.</summary>
    public sealed record RequestOpenProjectDialog : AppUiMsg;
    /// <summary>Load a specific project from a known path (used by File →
    /// recent / startup).</summary>
    public sealed record RequestOpenProject(string Path) : AppUiMsg;
    /// <summary>Switch to the given scene inside the active project.</summary>
    public sealed record RequestOpenScene(string ProjectRelative) : AppUiMsg;
    /// <summary>Re-read the active scene from disk, dropping unsaved edits.</summary>
    public sealed record RequestReloadScene : AppUiMsg;
    /// <summary>Ask the shell for a folder + pipeline id and create a new
    /// project on disk (with an empty default scene), then open it.</summary>
    public sealed record RequestNewProjectDialog : AppUiMsg;

    // ─── Project panel ─────────────────────────────────────────────────

    /// <summary>Toolbar-initiated rescan of the active project's on-disk tree.
    /// The reducer passes through; the shell rebuilds its <c>ProjectAssetIndex</c>.</summary>
    public sealed record RequestRescanProject : AppUiMsg;
    /// <summary>Open the OS file manager with the given absolute path selected
    /// (or its parent folder on Linux). View-only affordance; reducer is a no-op.</summary>
    public sealed record RequestRevealInExplorer(string AbsolutePath) : AppUiMsg;
}
