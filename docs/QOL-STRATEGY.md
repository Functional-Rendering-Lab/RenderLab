# Quality-of-Life Strategy

## Why QoL Before Papers

The PRD's success metric is: *"time from paper PDF open to first incorrect pixels on screen is under one evening session."* That timer doesn't start when you write the first shader — it starts when you need to orbit around the mesh to check a normal map, or drag a slider to find the right SSAO radius, or visualize the depth buffer to confirm your inputs are sane.

Without these tools, every paper implementation degrades into a printf-debugging loop: change a constant, recompile, squint at the result, repeat. The papers themselves are the hard part — the tooling around them should be invisible.

## Platform Strategy

**Desktop is the sole development target.** Learning rendering fundamentals is a desktop workflow: edit code, tweak parameters, inspect buffers, compare against reference images. Interactive tooling (free-fly camera, debug menus, buffer visualization) depends on mouse and keyboard, so it lives in `RenderLab.Platform.Desktop` and `RenderLab.Ui.ImGui`. Pure types in `RenderLab.Scene` stay backend-agnostic so a future platform can reuse them.

## What Was Added

### Scene Navigation
Free-fly camera controller with mouse-driven rotation and translation along the camera's local axes. The controller is a pure function (`FreeCameraState × CameraInput → FreeCameraState`) in `RenderLab.Scene`. Input polling lives in `RenderLab.Platform.Desktop`. ImGui gets input priority via `io.WantCaptureMouse` — debug panel interactions never leak into camera movement.

### Two-Way Debug Menus
`DebugFields` (in `RenderLab.Ui.ImGui`) bridges ImGui's `ref`-based API to a functional return-value style. Each helper takes an immutable value, shows a widget, returns the potentially modified value. Debug menus compose these into per-domain panels that follow the pattern `State → State`. The camera panel (`FreeCameraDebugMenu`) and visualization selector (`VisualizationDebugMenu`) are the first two.

Adding a new debug panel for a paper is one static method:
```csharp
public static SsaoParams Draw(SsaoParams p) =>
    p with {
        Radius = DebugFields.DragFloat("Radius", p.Radius, 0.01f, 0.01f, 5f),
        Bias   = DebugFields.DragFloat("Bias", p.Bias, 0.001f, 0f, 0.5f),
        Samples = DebugFields.SliderInt("Samples", p.Samples, 4, 64),
    };
```

### Buffer Visualization
Combo box to display any intermediate render target fullscreen: GBuffer position, normals, albedo, depth (log-scaled), or HDR pre-tonemap. Uses a dedicated `debugviz.frag` shader with a push-constant mode selector. The depth buffer is stored (`StoreOp.Store`) and transitioned to `DepthStencilReadOnlyOptimal` for sampling — this is also required by SSAO.

## Future QoL

Additional tooling will be added as papers demand it, not speculatively:

- **Shader hot-reload** — when iteration on a single shader dominates the feedback loop.
- **Screenshot / reference comparison** — when validating output against paper figures.
- **Keyboard shortcuts** — when switching between visualization modes or resetting camera becomes frequent enough to warrant hotkeys.
- **Per-pass toggle** — when a paper has enough passes that disabling individual ones aids debugging.
