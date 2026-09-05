# Quality-of-Life Strategy

## Why QoL Before Papers

The PRD's success metric is: *"time from paper PDF open to first incorrect pixels on screen is under one evening session."*
That timer doesn't start when you write the first shader.
It starts when you need to orbit around the mesh to check a normal map, or drag a slider to find the right SSAO radius, or visualize the depth buffer to confirm your inputs are sane.

Without these tools, every paper implementation degrades into a printf-debugging loop: change a constant, recompile, squint at the result, repeat.
The papers themselves are the hard part; the tooling around them should be invisible.

## Platform Strategy

**Desktop is the sole development target.**
Learning rendering fundamentals is a desktop workflow: edit code, tweak parameters, inspect buffers, compare against reference images.
Interactive tooling (free-fly camera, editor panels, buffer visualization) depends on mouse and keyboard, so it lives in `RenderLab.Platform.Desktop` and `RenderLab.Editor`.
Pure types in `RenderLab.Scene` stay backend-agnostic so a future platform can reuse them.

## What Was Added

### Scene Navigation
Free-fly camera controller with mouse-driven rotation and translation along the camera's local axes.
The controller is a pure function (`FreeCameraState × CameraInput → FreeCameraState`) in `RenderLab.Scene`.
Input polling lives in `RenderLab.Platform.Desktop`.
The editor gets input priority via the `UiIntent.WantCaptureMouse` flag it returns each frame, so panel interactions never leak into camera movement.
The rule is the viewport's rather than any panel's: the camera moves when the pointer is over the rendered image, and the interface owns the pointer everywhere else.

### Two-Way Editor Panels
Ptah's widgets return an `Edit<T>`: the current value plus a `Changed` flag.
A panel reads immutable state off `UiModel`, shows a widget, and dispatches a `UiMsg` when the widget reports a change.
The pure reducer in `RenderLab.Ui` folds that message into the next `UiModel`, so the panel never mutates anything itself.

Adding a new panel for a paper is one static method:
```csharp
internal static void Draw(WidgetKit w, WidgetState state, SsaoParams p, Action<UiMsg> dispatch)
{
    Edit<float> radius = w.DragFloat("Radius", p.Radius, 0.01f, 0.01f, 5f);
    if (radius.Changed)
        dispatch(new UiMsg.SetSsaoRadius(radius.Value));

    Edit<int> samples = w.SliderInt("Samples", p.Samples, 4, 64);
    if (samples.Changed)
        dispatch(new UiMsg.SetSsaoSamples(samples.Value));
}
```

The panels that exist today follow exactly this shape: Scene, Inspector, Lighting, Visualization, Asset Browser, Project, Render Graph, and GPU Timings, all in `RenderLab.Editor`.

### Buffer Visualization
Combo box to display any intermediate render target in the viewport: GBuffer position, normals, albedo, depth (log-scaled), or HDR pre-tonemap.
The list offered is the pipeline's `SupportedVisualizations`, not the whole enum, so the panel cannot name a mode the pipeline has no pass to resolve.
Uses a dedicated `debugviz.frag` shader with a push-constant mode selector.
The depth buffer is stored (`StoreOp.Store`) and transitioned to `DepthStencilReadOnlyOptimal` for sampling, which is also required by SSAO.

### Shader Hot-Reload
Save a `.vert` or `.frag` and the pipeline is rebuilt from fresh SPIR-V within a frame or two, with scene state, camera, and selection intact.
A failed compile keeps the previous pipeline live and logs `glslc` stderr.
See [`HOT-SHADER-RELOAD.md`](HOT-SHADER-RELOAD.md).

## Future QoL

Additional tooling will be added as papers demand it, not speculatively:

- **Screenshot / reference comparison**: when validating output against paper figures.
- **Keyboard shortcuts**: when switching between visualization modes or resetting camera becomes frequent enough to warrant hotkeys.
- **Per-pass toggle**: when a paper has enough passes that disabling individual ones aids debugging.
