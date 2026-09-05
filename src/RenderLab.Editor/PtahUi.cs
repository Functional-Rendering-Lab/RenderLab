using Ptah;
using Ptah.Backend;
using Ptah.Backend.Vulkan;
using Ptah.Functional;
using RenderLab.Gpu;
using RenderLab.Ui;
using Silk.NET.Input;
using Silk.NET.Vulkan;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.Editor;

/// <summary>What one frame of interface came to. Shown in the GPU Timings panel.</summary>
public readonly record struct UiCost(int Commands, int DrawCalls);

/// <summary>
/// The editor's Ptah shell, wired to the renderer FRL already owns. The counterpart of
/// <c>VulkanImGui</c>, and a great deal less of one: Ptah's Vulkan backend creates no device, no
/// swapchain and no frame loop, so what is left here is the atlas, the context, the input
/// translation, and the two points in a frame where the shell is built and recorded.
/// <para>
/// A frame is <see cref="Frame"/> - build the interface, get back what it wants - followed later
/// by <see cref="Record"/> - put it in the command buffer, over whatever the pipeline drew. They
/// are separate for the reason ImGui's <c>Render</c> and <c>RecordCommands</c> are: the interface
/// has to be built before the pipeline records, because building it is what produces the model
/// changes the pipeline then reads.
/// </para>
/// </summary>
public sealed class PtahUi : IDisposable
{
    private readonly GpuState _gpu;
    private readonly RenderPass _overlayPass;
    private readonly UIContext _ui;
    private readonly InputTracker _input;
    private readonly VulkanDrawTarget _target;

    private DrawList _frame = DrawList.Empty;
    private float _width = 1f;
    private float _height = 1f;
    private bool _reported;

    /// <summary>What the last recorded frame came to. Zero until one has been recorded.</summary>
    public UiCost Cost { get; private set; }

    /// <summary>
    /// The picture the viewport leaf draws: the image the renderer rendered the scene into. None
    /// until <see cref="ShowScene"/> has been called, and the same id from then on - the id is the
    /// interface's handle on the picture, and the picture behind it is replaced whenever the
    /// viewport changes size.
    /// </summary>
    public ImageId Scene { get; private set; } = ImageId.None;

    private PtahUi(GpuState gpu, RenderPass overlayPass, FontAtlas font, VulkanDrawTarget target,
        IInputContext input)
    {
        _gpu = gpu;
        _overlayPass = overlayPass;
        _target = target;
        _input = new InputTracker(input);

        // The measurer the layout runs on has to be the atlas the backend draws with, or text is
        // measured against one font and rendered in another.
        _ui = new UIContext((text, size) => font.Measure(text, size), EditorTheme.FontSize)
        {
            Clipboard = new GlfwClipboard(input),
        };
    }

    /// <summary>
    /// Brings up the shell against the application's own device and overlay pass.
    /// <paramref name="overlayPass"/> is the pass the interface is recorded into, and the only
    /// one that writes the swapchain: it has a single unresolved subpass, which is why the
    /// backend's subpass index and sample count are left at their defaults here.
    /// <para>
    /// Its color space is not left at a default, and cannot be. This swapchain is
    /// <c>B8G8R8A8Srgb</c>, because a renderer that lights a scene in linear space wants the
    /// hardware to encode on the way out; that same encode is applied to the interface recorded
    /// over the top, so the backend is told to hand over linear values rather than the bytes the
    /// theme names. Told wrong, every color in the editor arrives lifted, and a dark theme's
    /// chrome reaches the screen as a mid grey.
    /// </para>
    /// </summary>
    /// <param name="displayScale">
    /// Physical pixels per logical pixel on the monitor the window came up on. The atlas is
    /// rasterized that much larger while still reporting logical metrics, so text is sharp on a
    /// high-DPI screen instead of being baked at logical size and stretched by the backend.
    /// <c>PtahHost</c> does this from the window's content scale; an embedder has to do it
    /// itself, and not doing it is why the editor's glyphs were soft at 150%.
    /// <para>
    /// It is read once. Dragging the window to a monitor with a different scale leaves the atlas
    /// baked for the old one until the editor restarts - the same trade the swapchain makes, and
    /// the thing to revisit if this tool ever moves between panels mid-session.
    /// </para>
    /// </param>
    public static Result<PtahUi, PtahStartupError> Create(
        GpuState gpu, RenderPass overlayPass, IInputContext input, float displayScale) =>
        FontAtlas.Default(EditorTheme.FontSize, displayScale).Bind(font =>
            VulkanDrawTarget.Create(Context(gpu), font, overlayPass,
                    VulkanDrawTarget.ColorSpaceOf(gpu.SwapchainFormat))
                .Map(target => new PtahUi(gpu, overlayPass, font, target, input)));

    /// <summary>
    /// Everything the backend borrows from the application, and the complete list of it. Nothing
    /// here is created or destroyed by Ptah; it all has to outlive the shell.
    /// </summary>
    private static VulkanContext Context(GpuState gpu) => new(
        Api: gpu.Vk,
        PhysicalDevice: gpu.PhysicalDevice,
        Device: gpu.Device,
        GraphicsQueue: gpu.GraphicsQueue,
        CommandPool: gpu.CommandPool,
        FramesInFlight: GpuState.MaxFramesInFlight);

    /// <summary>
    /// Points the viewport's picture at <paramref name="view"/>, registering it the first time.
    /// <para>
    /// It is one id for the life of the editor because the display list refers to the picture by
    /// id and nothing else: a viewport resize throws away the image and makes another, and the
    /// interface is not supposed to notice. The caller has to have idled the device first - this
    /// rewrites a descriptor set a frame in flight may still be reading.
    /// </para>
    /// </summary>
    public Result<Unit, PtahStartupError> ShowScene(ImageView view, Sampler sampler)
    {
        if (Scene.IsNone)
            return _target.RegisterImage(view, sampler).Map(id =>
            {
                Scene = id;
                return Unit.Value;
            });

        _target.UpdateImage(Scene, view, sampler);
        return Result<Unit, PtahStartupError>.Ok(Unit.Value);
    }

    /// <summary>
    /// Builds one frame of interface: takes this frame's input, runs <paramref name="build"/>
    /// between the context's own begin and end, and keeps the display list for
    /// <see cref="Record"/>. What the build hands back is the view's result, folded by the shell
    /// exactly like the ImGui one it is drawn beside.
    /// </summary>
    /// <param name="width">The client area's width in logical pixels.</param>
    /// <param name="height">Its height.</param>
    /// <param name="dt">Seconds since the last frame.</param>
    /// <param name="build">Builds the interface and reports what it wants.</param>
    public UiViewResult Frame(int width, int height, float dt,
        Func<UIContext, UiViewResult> build)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        // The whole client area. It used to start below Dear ImGui's menu bar, because two
        // layouts drawn over each other had to agree about where the workspace began; there is
        // one layout now, and it owns its own top strip.
        _ui.BeginBuild(dt, new Rect(0f, 0f, _width, _height), _input.Snapshot());
        UiViewResult result = build(_ui);
        _ui.EndBuild();

        _frame = _ui.BuildDrawList();
        return result;
    }

    /// <summary>
    /// Records the frame built by <see cref="Frame"/> into <paramref name="cmd"/>, in the pass
    /// that owns the swapchain image.
    /// <para>
    /// The pass is begun whether or not there is anything to draw. It used to be skipped for an
    /// empty display list, back when it loaded a frame the renderer had already drawn and skipping
    /// it left that frame alone; it clears now, and a frame that begins no pass is a swapchain
    /// image presented in an undefined layout with undefined contents.
    /// </para>
    /// </summary>
    public unsafe void Record(CommandBuffer cmd, Framebuffer framebuffer, Extent2D extent)
    {
        // What shows where the interface does not paint. Nothing should: the shell fills the
        // client area. It is the ground under a frame that failed to build rather than a colour
        // the editor is meant to wear.
        var clear = new ClearValue(new ClearColorValue(0f, 0f, 0f, 1f));

        var begin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _overlayPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), extent),
            ClearValueCount = 1,
            PClearValues = &clear,
        };

        Vk vk = _gpu.Vk;
        vk.CmdBeginRenderPass(cmd, in begin, SubpassContents.Inline);

        if (_frame.Count == 0)
        {
            vk.CmdEndRenderPass(cmd);
            Cost = default;
            return;
        }

        // The display list is in logical pixels and the attachment is in physical ones; the
        // backend maps between them, which is the whole of what a high-DPI frame needs.
        _target.BeginFrame(_width, _height, extent, _gpu.CurrentFrame);

        _frame.SubmitTo(_target)
            .Bind(_ => _target.EndFrame(cmd))
            .Match(ok => ok, Report);

        vk.CmdEndRenderPass(cmd);

        Cost = new UiCost(_frame.Count, _target.DrawCallCount);
    }

    /// <summary>
    /// Says a recording failure once. A frame that cannot record is a frame that will not record
    /// next time either, and sixty lines a second of the same message is how the first one gets
    /// lost.
    /// </summary>
    private Unit Report(DrawError error)
    {
        if (!_reported)
        {
            _reported = true;
            Console.Error.WriteLine($"  ui: {error}");
        }

        return Unit.Value;
    }

    public void Dispose() => _target.Dispose();
}
