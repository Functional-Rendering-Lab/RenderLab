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
}
