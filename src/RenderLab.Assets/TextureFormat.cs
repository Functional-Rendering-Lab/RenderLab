namespace RenderLab.Assets;

/// <summary>
/// Pixel encoding of a <see cref="TextureAsset"/>. The shell maps this to a
/// concrete Vulkan format. <see cref="Rgba8Srgb"/> is the right choice for
/// albedo/colour maps so sampling auto-decodes to linear; <see cref="Rgba8Unorm"/>
/// is for data textures (normal maps, masks, etc.).
/// </summary>
public enum TextureFormat
{
    Rgba8Unorm,
    Rgba8Srgb,
}
