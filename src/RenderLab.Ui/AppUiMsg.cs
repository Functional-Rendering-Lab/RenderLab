namespace RenderLab.Ui;

/// <summary>
/// Shell-scope messages emitted by the menu bar: which demo to switch to, which
/// panel to show/hide, and an exit request. Folded into <see cref="AppUiModel"/>
/// by <see cref="AppUiUpdate.Apply"/>.
/// </summary>
public abstract record AppUiMsg
{
    public sealed record RequestSwitchDemo(DemoId Id) : AppUiMsg;
    public sealed record TogglePanel(PanelId Id) : AppUiMsg;
    public sealed record SetPanelVisible(PanelId Id, bool Visible) : AppUiMsg;
    public sealed record RequestExit : AppUiMsg;
    /// <summary>One-shot import trigger (registry side-effect). The reducer
    /// passes through; the shell calls <c>AssetRegistry.ImportGltf</c> and
    /// dispatches <c>UiMsg.AddDrawable</c> for each imported drawable.</summary>
    public sealed record RequestImportGltf(string Path) : AppUiMsg;
    /// <summary>Open the OS file picker, then import the chosen file. The
    /// reducer passes through; the shell owns the dialog because the picker
    /// is platform-specific.</summary>
    public sealed record RequestImportGltfDialog : AppUiMsg;
    /// <summary>Remove a registered asset. Reducer passes through; the shell
    /// validates reference safety, calls the registry, and invalidates any
    /// caches keyed on the id.</summary>
    public sealed record RequestRemoveMesh(RenderLab.Assets.MeshId Id) : AppUiMsg;
    public sealed record RequestRemoveTexture(RenderLab.Assets.TextureId Id) : AppUiMsg;
    public sealed record RequestRemoveMaterial(RenderLab.Assets.MaterialId Id) : AppUiMsg;
}
