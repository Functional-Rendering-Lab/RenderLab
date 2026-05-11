using System.Diagnostics;
using NativeFileDialogSharp;

namespace RenderLab.Platform.Desktop;

/// <summary>
/// Thin wrapper over NativeFileDialogSharp so the rest of the engine doesn't
/// have to know which native picker library is in use. All methods block the
/// calling thread until the dialog is dismissed; only call from the main loop.
/// </summary>
public static class PlatformDialogs
{
    /// <summary>
    /// Opens an OS file picker for glTF / glb files. Returns the selected path,
    /// or <c>null</c> if the user cancelled or the dialog failed.
    /// </summary>
    public static string? OpenGltfFile()
    {
        var result = Dialog.FileOpen("glb,gltf");
        return result.IsOk ? result.Path : null;
    }

    /// <summary>
    /// Opens an OS folder picker. Returns the selected absolute path, or
    /// <c>null</c> if the user cancelled or the dialog failed. Used for the
    /// File → Open Project… menu item.
    /// </summary>
    public static string? OpenFolder(string? defaultPath = null)
    {
        var result = Dialog.FolderPicker(defaultPath);
        return result.IsOk ? result.Path : null;
    }

    /// <summary>
    /// Opens an OS save-file dialog filtered to <c>*.scene.json</c>. Returns
    /// the chosen absolute path, or <c>null</c> if cancelled.
    /// </summary>
    /// <param name="defaultDirectory">Initial directory the dialog opens in
    /// (e.g. the active project's <c>scenes/</c> folder).</param>
    /// <param name="defaultName">Suggested filename without extension.</param>
    public static string? SaveSceneFile(string? defaultDirectory, string? defaultName)
    {
        var result = Dialog.FileSave("scene.json", defaultDirectory);
        if (!result.IsOk) return null;
        var path = result.Path ?? "";
        // NFD does not always append the extension on every platform; force
        // .scene.json so callers don't have to worry about case mismatches.
        if (!path.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase))
            path += ".scene.json";
        return path;
    }

    /// <summary>
    /// Opens the OS file manager with <paramref name="absolutePath"/> selected
    /// (Windows / macOS) or the parent folder opened (Linux best-effort). Silent
    /// on failure — this is a non-fatal UX affordance.
    /// </summary>
    public static void RevealInExplorer(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath)) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", $"/select,\"{absolutePath}\"");
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", new[] { "-R", absolutePath });
            else
                Process.Start("xdg-open", Path.GetDirectoryName(absolutePath) ?? "");
        }
        catch { /* swallow */ }
    }
}
