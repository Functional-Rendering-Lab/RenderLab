namespace RenderLab.Scene;

/// <summary>
/// Selects which BRDF the deferred lighting pass evaluates. Values are the
/// integer codes the lighting shader branches on.
/// </summary>
public enum ShadingMode
{
    Lambertian = 0,
    Phong = 1,
    BlinnPhong = 2,
}
