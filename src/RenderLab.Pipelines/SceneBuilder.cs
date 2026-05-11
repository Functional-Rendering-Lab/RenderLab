using System.Collections.Immutable;
using RenderLab.Scene;
using RenderLab.Ui;

namespace RenderLab.Pipelines;

using Scene = RenderLab.Scene.Scene;

/// <summary>
/// Pure projection from the editable <see cref="UiModel"/> into the immutable
/// <see cref="Scene"/> snapshot consumed by pass recorders. Camera goes
/// through <see cref="FreeCameraController.ToCamera"/> with the supplied
/// aspect; drawables drop the editor-only <c>LocalId</c>; lights pass through
/// unchanged.
/// </summary>
public static class SceneBuilder
{
    public static Scene BuildScene(UiModel ui, float aspect)
    {
        var drawables = ImmutableArray.CreateBuilder<Drawable>(ui.Drawables.Length);
        foreach (var d in ui.Drawables)
            drawables.Add(new Drawable(d.Name, d.Mesh, d.Transform, d.Material));
        return new Scene(
            Camera: FreeCameraController.ToCamera(ui.Camera, aspect),
            Drawables: drawables.MoveToImmutable(),
            Lights: ui.Lights);
    }
}
