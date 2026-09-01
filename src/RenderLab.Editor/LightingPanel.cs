using Ptah.Widgets;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Editor;

/// <summary>
/// The shading model, and whether albedo is dropped from the result. Light positions, colours,
/// ambient and the clear colour are edited in the Inspector - select a light, or the Environment
/// entry in the Scene panel - so what is left here is the two toggles that are about the renderer
/// rather than about the scene.
/// </summary>
internal static class LightingPanel
{
    /// <summary>
    /// The three BRDFs, in the order <see cref="ShadingMode"/> declares them. Written out rather
    /// than taken from the enum's names because "Blinn-Phong (N·H)" says which dot product is
    /// being taken and <c>BlinnPhong</c> does not, and that is the entire question this control
    /// asks.
    /// </summary>
    private static readonly string[] ShadingModes =
    [
        "Lambertian (diffuse)",
        "Phong (R·V)",
        "Blinn-Phong (N·H)",
    ];

    internal static void Draw(WidgetKit w, WidgetState state, UiModel model, Action<UiMsg> dispatch)
    {
        Edit<int> mode = w.Combo(state.Popups, "Model", (int)model.Shading, ShadingModes);
        if (mode.Changed)
            dispatch(new UiMsg.SetShading((ShadingMode)mode.Value));

        Edit<bool> lightingOnly = w.Checkbox("Lighting only", model.LightingOnly);
        if (lightingOnly.Changed)
            dispatch(new UiMsg.SetLightingOnly(lightingOnly.Value));
    }
}
