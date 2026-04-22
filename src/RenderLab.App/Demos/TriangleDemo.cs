using System.Numerics;
using ImGuiNET;
using Silk.NET.Vulkan;
using RenderLab.Debug;
using RenderLab.Gpu;
using RenderLab.Platform.Desktop;
using RenderLab.Ui;
using Buffer = Silk.NET.Vulkan.Buffer;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.App.Demos;

// ─── Minimal Modern Rendering Pipeline ──────────────────────────────
// Matches blog post 2: "From Nothing to a Triangle."
//
// Demonstrates the eight concepts needed to put a single triangle on
// screen with a modern GPU API:
//   1. GPU connection (instance, physical device, logical device, queues)
//   2. Swapchain (images to render into)
//   3. Shaders (vertex + fragment, compiled to SPIR-V)
//   4. Vertex layout (position vec2 + color vec3)
//   5. Render pass (clear → draw → present)
//   6. Graphics pipeline (immutable rendering configuration)
//   7. Vertex buffer (triangle data on the GPU)
//   8. Frame loop (acquire, record, submit, present, synchronize)
//
// The core pipeline is still one pass, one draw. A second "overlay" render
// pass (LoadOp.Load → Store) is appended solely to host the app shell's
// ImGui menu bar so the user can navigate back to other demos — it does not
// touch the pedagogical triangle pipeline.

public sealed class TriangleDemo : IDemo
{
    const int WindowWidth = 1280;
    const int WindowHeight = 720;

    // Platform + GPU
    DesktopWindow window = null!;
    Vk vk = null!;
    GpuState gpu = null!;

    // Pipeline objects (created once)
    RenderPass renderPass;
    Pipeline pipeline;
    PipelineLayout pipelineLayout;

    // Vertex buffer
    Buffer vertexBuffer;
    Allocation vertexAlloc;

    // Transient resources (recreated on swapchain resize)
    Framebuffer[] framebuffers = [];
    Framebuffer[] overlayFramebuffers = [];

    // App shell overlay — isolated from the pedagogical triangle pipeline
    VulkanImGui imgui = null!;
    RenderPass overlayRenderPass;
    AppUiModel app = AppUiModel.Default(DemoId.Triangle);

    public DemoId? Run(AppUiModel initialApp)
    {
        app = initialApp;
        Init();

        var frameTimer = System.Diagnostics.Stopwatch.StartNew();
        double lastFrameTime = 0;

        // ─── 8. Frame loop ───────────────────────────────────────────
        // Every frame: acquire an image, record commands, submit, present.
        // Two frames in flight — the CPU can record frame N while the GPU
        // executes frame N-1. Fences prevent the CPU from racing too far ahead.

        while (!window.IsClosing)
        {
            if (app.RequestedExit) return null;
            if (app.RequestedDemo is { } switchTo) return switchTo;

            window.DoEvents();

            if (window.Width == 0 || window.Height == 0) continue;

            if (window.WasResized || gpu.FramebufferResized)
            {
                window.ClearResizeFlag();
                gpu.FramebufferResized = false;
                RecreateSwapchainResources();
                continue;
            }

            double currentTime = frameTimer.Elapsed.TotalSeconds;
            float deltaTime = (float)(currentTime - lastFrameTime);
            lastFrameTime = currentTime;

            // Feed input to ImGui so the menu bar is clickable
            var input = window.PollInput();
            var io = ImGui.GetIO();
            io.MousePos = input.MousePosition;
            io.MouseDown[0] = input.LeftButtonDown;
            io.MouseDown[1] = input.RightButtonDown;
            io.MouseDown[2] = input.MiddleButtonDown;
            io.MouseWheel = input.ScrollDelta;

            // Step 1: Acquire the next swapchain image
            if (!VulkanFrame.BeginFrame(gpu, out var imageIndex))
            {
                RecreateSwapchainResources();
                continue;
            }

            // Step 2: Record commands — triangle pass, then overlay menu bar
            var cmd = gpu.CommandBuffers[gpu.CurrentFrame];
            RecordCommands(cmd, imageIndex);
            RecordOverlayPass(cmd, imageIndex, deltaTime);

            // Step 3: Submit and present
            if (!VulkanFrame.EndFrame(gpu, imageIndex))
                RecreateSwapchainResources();
        }

        return null;
    }

    void RecordOverlayPass(CommandBuffer cmd, uint imageIndex, float dt)
    {
        imgui.NewFrame(window.Width, window.Height, dt);

        var appMessages = new List<AppUiMsg>();
        AppMenuBar.Draw(app, appMessages.Add, includeViewMenu: false);

        app = AppUiUpdate.ApplyAll(app, appMessages);

        imgui.RecordCommands(vk, cmd, overlayRenderPass,
            overlayFramebuffers[imageIndex], gpu.SwapchainExtent);
    }

    void Init()
    {
        // ─── 1. GPU connection ───────────────────────────────────────
        // Instance → Physical device → Logical device → Queues.
        // The handshake between the application and the GPU.

        Console.WriteLine("RenderLab — Minimal Pipeline (Triangle)");

        window = DesktopWindow.Create("RenderLab — Triangle", WindowWidth, WindowHeight);
        vk = Vk.GetApi();
        gpu = VulkanDevice.Create(vk, window.GetRequiredVulkanExtensions(),
            instance => window.CreateVulkanSurface(instance));

        // ─── 2. Swapchain ────────────────────────────────────────────
        // Created inside VulkanDevice.Create — a set of images the GPU
        // renders into while the display shows a previously finished one.

        Console.WriteLine($"  Swapchain: {gpu.SwapchainExtent.Width}x{gpu.SwapchainExtent.Height}");

        // ─── 3. Shaders ─────────────────────────────────────────────
        // Vertex shader: passes position through, forwards color.
        // Fragment shader: outputs interpolated color.
        // Both compiled offline from GLSL to SPIR-V.

        var shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
        var vertModule = VulkanPipeline.CreateShaderModule(gpu,
            File.ReadAllBytes(Path.Combine(shaderDir, "triangle.vert.spv")));
        var fragModule = VulkanPipeline.CreateShaderModule(gpu,
            File.ReadAllBytes(Path.Combine(shaderDir, "triangle.frag.spv")));

        // ─── 5. Render pass ─────────────────────────────────────────
        // Single color attachment: clear → draw → present.
        // Declares what happens to the image, not how to draw into it.

        renderPass = VulkanPipeline.CreateRenderPass(gpu);

        // ─── 6. Graphics pipeline ────────────────────────────────────
        // The complete, immutable description of how rendering happens:
        // shaders + vertex layout + input assembly + viewport + rasterizer
        // + blending + render pass. Created once, bound at draw time.
        //
        // Uses the Vertex type (vec2 position + vec3 color) which matches
        // the triangle.vert shader layout exactly.

        pipeline = VulkanPipeline.CreateGraphicsPipeline(
            gpu, renderPass, vertModule, fragModule, out pipelineLayout);

        // Shader modules can be destroyed after pipeline creation —
        // the compiled pipeline retains the GPU machine code.
        unsafe
        {
            vk.DestroyShaderModule(gpu.Device, vertModule, null);
            vk.DestroyShaderModule(gpu.Device, fragModule, null);
        }

        // ─── 4 & 7. Vertex layout + buffer ──────────────────────────
        // Three vertices, each with a 2D position and an RGB color.
        // Uploaded to GPU-visible memory so the vertex shader can read them.

        ReadOnlySpan<Vertex> vertices =
        [
            new(new Vector2(-0.5f, -0.5f), new Vector3(1f, 0f, 0f)), // bottom-left,  red
            new(new Vector2( 0.5f, -0.5f), new Vector3(0f, 1f, 0f)), // bottom-right, green
            new(new Vector2( 0.0f,  0.5f), new Vector3(0f, 0f, 1f)), // top-center,   blue
        ];

        (vertexBuffer, vertexAlloc) = VulkanBuffer.Create<Vertex>(
            gpu, BufferUsageFlags.VertexBufferBit, vertices);

        Console.WriteLine("  Vertices: 3 (RGB triangle)");

        // Create framebuffers for each swapchain image
        framebuffers = VulkanPipeline.CreateFramebuffers(gpu, renderPass);

        // ─── App shell overlay (isolated from the triangle pipeline) ────
        overlayRenderPass = VulkanPipeline.CreateOverlayRenderPass(gpu);
        overlayFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, overlayRenderPass);
        imgui = VulkanImGui.Create(gpu, overlayRenderPass);
    }

    // ─── Command recording ───────────────────────────────────────────
    // Deterministic given the same inputs: pipeline, buffer, viewport.
    // The command buffer is a recording of work — the GPU executes it
    // only when submitted.

    unsafe void RecordCommands(CommandBuffer cmd, uint imageIndex)
    {
        var clearColor = new ClearValue(new ClearColorValue(0.1f, 0.1f, 0.1f, 1.0f));

        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = framebuffers[imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent),
            ClearValueCount = 1,
            PClearValues = &clearColor,
        };

        vk.CmdBeginRenderPass(cmd, &renderPassBegin, SubpassContents.Inline);

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);

        // Dynamic viewport and scissor — set each frame so resize works
        var viewport = new Viewport(0, 0,
            gpu.SwapchainExtent.Width, gpu.SwapchainExtent.Height, 0, 1);
        vk.CmdSetViewport(cmd, 0, 1, &viewport);

        var scissor = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent);
        vk.CmdSetScissor(cmd, 0, 1, &scissor);

        // Bind the triangle's vertex buffer and draw
        var vb = vertexBuffer;
        ulong offset = 0;
        vk.CmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);

        vk.CmdDraw(cmd, 3, 1, 0, 0);

        vk.CmdEndRenderPass(cmd);
    }

    // ─── Swapchain resize ────────────────────────────────────────────

    void RecreateSwapchainResources()
    {
        vk.DeviceWaitIdle(gpu.Device);
        VulkanPipeline.DestroyFramebuffers(gpu, framebuffers);
        VulkanPipeline.DestroyFramebuffers(gpu, overlayFramebuffers);
        VulkanDevice.DestroyRenderFinishedSemaphores(gpu);
        VulkanSwapchain.Recreate(gpu, (uint)window.Width, (uint)window.Height);
        VulkanDevice.CreateRenderFinishedSemaphores(gpu);
        framebuffers = VulkanPipeline.CreateFramebuffers(gpu, renderPass);
        overlayFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, overlayRenderPass);
    }

    // ─── Cleanup ─────────────────────────────────────────────────────

    public unsafe void Dispose()
    {
        vk.DeviceWaitIdle(gpu.Device);

        imgui.Dispose();
        VulkanPipeline.DestroyFramebuffers(gpu, overlayFramebuffers);
        vk.DestroyRenderPass(gpu.Device, overlayRenderPass, null);

        VulkanPipeline.DestroyFramebuffers(gpu, framebuffers);
        vk.DestroyPipeline(gpu.Device, pipeline, null);
        vk.DestroyPipelineLayout(gpu.Device, pipelineLayout, null);
        vk.DestroyRenderPass(gpu.Device, renderPass, null);
        VulkanBuffer.Destroy(gpu, vertexBuffer, vertexAlloc);

        gpu.Dispose();
        window.Dispose();
    }
}
