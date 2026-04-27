using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.Vulkan;
using RenderLab.Ui.ImGui;
using RenderLab.Gpu;
using RenderLab.Papers;
using RenderLab.Ui;
using RenderLab.Platform.Desktop;
using RenderLab.Scene;
using Buffer = Silk.NET.Vulkan.Buffer;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.App.Demos;

// ─── Post 3: G-Buffer Only ──────────────────────────────────────────
// Matches blog post 3: "What a Frame Knows Before It Sees the Light."
//
// Renders scene geometry into structured G-Buffer textures (position,
// normal, albedo, depth) and visualizes each buffer directly — no
// lighting, no tonemap, no render graph. The screen stays "dark" in
// the narrative sense: the data is there, but no light has touched it.
//
// Pipeline: GBuffer pass → manual barriers → Debug viz → ImGui overlay
//
// Manual barriers replace the render graph compiler (which is Post 4's
// story). This demo shows the cost of hand-managed synchronization
// that the graph automates.

public sealed class GBufferDemo : IDemo
{
    const int WindowWidth = 1280;
    const int WindowHeight = 720;
    const float RotateSensitivity = 0.005f;
    const float PanSensitivity = 0.01f;
    const float ZoomSensitivity = 0.3f;

    // Valid visualization modes for this demo (no Final or HDR)
    static readonly string[] ModeNames = ["Position", "Normal", "Albedo", "Depth"];
    static readonly VisualizationMode[] Modes =
    [
        VisualizationMode.Position, VisualizationMode.Normal,
        VisualizationMode.Albedo, VisualizationMode.Depth,
    ];

    // ─── Owned resources ─────────────────────────────────────────────
    DesktopWindow window = null!;
    Vk vk = null!;
    GpuState gpu = null!;

    // Mesh
    uint indexCount;
    Buffer vertexBuffer, indexBuffer;
    Allocation vertexAlloc, indexAlloc;

    // Render passes
    RenderPass gbufferRenderPass;     // 3 color + depth
    RenderPass swapchainRenderPass;   // single color, for debug viz output
    RenderPass overlayRenderPass;     // LoadOp.Load for ImGui

    // Pipelines
    Pipeline gbufferPipeline;
    PipelineLayout gbufferPipelineLayout;
    Pipeline debugVizPipeline;
    PipelineLayout debugVizPipelineLayout;

    // Descriptor layout
    DescriptorSetLayout singleDsLayout;

    // Camera
    FreeCameraState cameraState;
    Camera camera = null!;
    VisualizationMode vizMode = VisualizationMode.Position;

    // Transient resources (recreated on resize)
    Sampler sampler;
    Image gbufferPosImage, gbufferNormImage, gbufferAlbImage, depthImage;
    Allocation gbufferPosAlloc, gbufferNormAlloc, gbufferAlbAlloc, depthAlloc;
    ImageView gbufferPosView, gbufferNormView, gbufferAlbView, depthView;
    Framebuffer gbufferFramebuffer;
    Framebuffer[] swapchainFramebuffers = [];
    Framebuffer[] overlayFramebuffers = [];
    DescriptorPool debugVizDescPool;
    DescriptorSet[] debugVizPositionSets = [], debugVizNormalSets = [];
    DescriptorSet[] debugVizAlbedoSets = [], debugVizDepthSets = [];

    // ImGui
    VulkanImGui imgui = null!;

    // App-shell model (menu bar dispatches messages into this)
    AppUiModel app = AppUiModel.Default(DemoId.GBuffer);

    public DemoId? Run(AppUiModel initialApp)
    {
        app = initialApp;
        Init();

        var frameTimer = System.Diagnostics.Stopwatch.StartNew();
        double lastFrameTime = 0;

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

            var input = window.PollInput();
            var io = ImGui.GetIO();

            if (!io.WantCaptureMouse)
            {
                var cameraInput = new CameraInput(
                    YawDelta: input.LeftButtonDown ? -input.MouseDelta.X * RotateSensitivity : 0,
                    PitchDelta: input.LeftButtonDown ? -input.MouseDelta.Y * RotateSensitivity : 0,
                    MoveDelta: new Vector3(
                        input.MiddleButtonDown ? -input.MouseDelta.X * PanSensitivity : 0,
                        input.MiddleButtonDown ?  input.MouseDelta.Y * PanSensitivity : 0,
                        input.ScrollDelta * ZoomSensitivity));

                cameraState = FreeCameraController.Update(cameraState, cameraInput);
                camera = FreeCameraController.ToCamera(cameraState, (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height);
            }

            io.MousePos = input.MousePosition;
            io.MouseDown[0] = input.LeftButtonDown;
            io.MouseDown[1] = input.RightButtonDown;
            io.MouseDown[2] = input.MiddleButtonDown;
            io.MouseWheel = input.ScrollDelta;

            if (!VulkanFrame.BeginFrame(gpu, out var imageIndex))
            {
                RecreateSwapchainResources();
                continue;
            }

            var cmd = gpu.CommandBuffers[gpu.CurrentFrame];

            // GBuffer pass → manual barriers → debug viz → ImGui
            RecordGBufferPass(vk, cmd);
            InsertGBufferBarriers(vk, cmd);
            RecordDebugVizPass(vk, cmd, imageIndex);
            RecordImGuiPass(vk, cmd, imageIndex, deltaTime);

            if (!VulkanFrame.EndFrame(gpu, imageIndex))
                RecreateSwapchainResources();
        }

        return null;
    }

    void Init()
    {
        // ─── Load mesh ───────────────────────────────────────────────
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
        var objPath = Path.Combine(assetsDir, "suzanne.obj");
        var mesh = File.Exists(objPath) ? ObjLoader.Load(objPath) : ObjLoader.CreateCube();
        indexCount = (uint)mesh.Indices.Length;

        Console.WriteLine("RenderLab — Post 3: G-Buffer Visualization");
        Console.WriteLine($"  Mesh: {mesh.Vertices.Length} vertices, {mesh.Indices.Length / 3} triangles");

        // ─── Platform + GPU ──────────────────────────────────────────
        window = DesktopWindow.Create("RenderLab — G-Buffer", WindowWidth, WindowHeight);
        vk = Vk.GetApi();
        gpu = VulkanDevice.Create(vk, window.GetRequiredVulkanExtensions(),
            instance => window.CreateVulkanSurface(instance));

        // ─── Upload mesh ─────────────────────────────────────────────
        (vertexBuffer, vertexAlloc) = VulkanBuffer.Create<Vertex3D>(gpu, BufferUsageFlags.VertexBufferBit,
            mesh.Vertices);
        (indexBuffer, indexAlloc) = VulkanBuffer.Create<uint>(gpu, BufferUsageFlags.IndexBufferBit,
            mesh.Indices);

        // ─── Shaders ─────────────────────────────────────────────────
        var shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
        byte[] LoadSpv(string name) => File.ReadAllBytes(Path.Combine(shaderDir, name));

        var gbufferVertModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.vert.spv"));
        var gbufferFragModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.frag.spv"));
        var fsVertModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("fullscreen.vert.spv"));
        var debugVizFragModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("debugviz.frag.spv"));

        // ─── Render passes ───────────────────────────────────────────
        gbufferRenderPass = VulkanPipeline.CreateGBufferRenderPass(gpu);
        swapchainRenderPass = VulkanPipeline.CreateRenderPass(gpu);
        overlayRenderPass = VulkanPipeline.CreateOverlayRenderPass(gpu);

        // ─── Descriptor layout ───────────────────────────────────────
        singleDsLayout = VulkanDescriptors.CreateSamplerLayout(gpu);

        // ─── Pipelines ───────────────────────────────────────────────
        gbufferPipeline = VulkanPipeline.CreateGBufferPipeline(
            gpu, gbufferRenderPass, gbufferVertModule, gbufferFragModule,
            Vertex3D.BindingDescription, Vertex3D.AttributeDescriptions,
            (uint)Marshal.SizeOf<GBufferPushConstants>(),
            out gbufferPipelineLayout);

        debugVizPipeline = VulkanPipeline.CreateFullscreenPipeline(
            gpu, swapchainRenderPass, singleDsLayout, fsVertModule, debugVizFragModule,
            (uint)Marshal.SizeOf<DebugVizPushConstants>(), ShaderStageFlags.FragmentBit,
            out debugVizPipelineLayout);

        unsafe
        {
            vk.DestroyShaderModule(gpu.Device, gbufferVertModule, null);
            vk.DestroyShaderModule(gpu.Device, gbufferFragModule, null);
            vk.DestroyShaderModule(gpu.Device, fsVertModule, null);
            vk.DestroyShaderModule(gpu.Device, debugVizFragModule, null);
        }

        // ─── Camera ──────────────────────────────────────────────────
        cameraState = FreeCameraController.CreateDefault();
        camera = FreeCameraController.ToCamera(cameraState, (float)WindowWidth / WindowHeight);

        // ─── Transient resources ─────────────────────────────────────
        sampler = VulkanImage.CreateSampler(gpu);
        CreateTransientResources();

        // ─── ImGui ───────────────────────────────────────────────────
        imgui = VulkanImGui.Create(gpu, overlayRenderPass);

        Console.WriteLine($"  Swapchain: {gpu.SwapchainExtent.Width}x{gpu.SwapchainExtent.Height}");
        Console.WriteLine("  No render graph — manual barriers between passes");
    }

    // ─── GBuffer pass ────────────────────────────────────────────────
    // Writes position, normal, albedo to 3 color attachments + depth.
    // Identical to DeferredDemo — same geometry, same shader, same data.

    void RecordGBufferPass(Vk api, CommandBuffer cb)
    {
        var resources = new GBufferPassResources(
            RenderPass: gbufferRenderPass,
            Framebuffer: gbufferFramebuffer,
            Pipeline: gbufferPipeline,
            PipelineLayout: gbufferPipelineLayout,
            Extent: gpu.SwapchainExtent);

        var pc = GBufferPass.BuildPushConstants(Transform.Default, camera, MaterialParams.Default);
        GBufferPass.Record(api, cb, resources, pc, vertexBuffer, indexBuffer, indexCount);
    }

    // ─── Manual barriers ─────────────────────────────────────────────
    // Without a render graph, we insert barriers by hand.
    // The GBuffer render pass leaves color attachments in
    // ColorAttachmentOptimal — we transition them to ShaderReadOnly
    // so the debug viz fragment shader can sample them.

    unsafe void InsertGBufferBarriers(Vk api, CommandBuffer cb)
    {
        var barriers = stackalloc ImageMemoryBarrier[3];

        barriers[0] = MakeColorBarrier(gbufferPosImage);
        barriers[1] = MakeColorBarrier(gbufferNormImage);
        barriers[2] = MakeColorBarrier(gbufferAlbImage);

        api.CmdPipelineBarrier(cb,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 3, barriers);

        // Depth needs a separate barrier only when we want to sample it
        if (vizMode == VisualizationMode.Depth)
        {
            var depthBarrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.DepthStencilAttachmentOptimal,
                NewLayout = ImageLayout.DepthStencilReadOnlyOptimal,
                SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
                DstAccessMask = AccessFlags.ShaderReadBit,
                Image = depthImage,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    BaseMipLevel = 0, LevelCount = 1,
                    BaseArrayLayer = 0, LayerCount = 1,
                },
            };
            api.CmdPipelineBarrier(cb,
                PipelineStageFlags.LateFragmentTestsBit,
                PipelineStageFlags.FragmentShaderBit,
                0, 0, null, 0, null, 1, &depthBarrier);
        }

        static ImageMemoryBarrier MakeColorBarrier(Image image) => new()
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.ColorAttachmentOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0, LevelCount = 1,
                BaseArrayLayer = 0, LayerCount = 1,
            },
        };
    }

    // ─── Debug visualization pass ────────────────────────────────────
    // Renders one G-Buffer texture to the swapchain via a fullscreen
    // triangle. No lighting, no tonemap — just raw buffer data.

    unsafe void RecordDebugVizPass(Vk api, CommandBuffer cb, uint imageIndex)
    {
        var sourceSet = vizMode switch
        {
            VisualizationMode.Position => debugVizPositionSets[gpu.CurrentFrame],
            VisualizationMode.Normal => debugVizNormalSets[gpu.CurrentFrame],
            VisualizationMode.Albedo => debugVizAlbedoSets[gpu.CurrentFrame],
            VisualizationMode.Depth => debugVizDepthSets[gpu.CurrentFrame],
            _ => debugVizPositionSets[gpu.CurrentFrame],
        };
        var resources = new DebugVizPassResources(
            RenderPass: swapchainRenderPass,
            Framebuffer: swapchainFramebuffers[imageIndex],
            Pipeline: debugVizPipeline,
            PipelineLayout: debugVizPipelineLayout,
            SourceSet: sourceSet,
            Extent: gpu.SwapchainExtent);
        var pc = DebugVizPass.BuildPushConstants(vizMode == VisualizationMode.Depth, camera);
        DebugVizPass.Record(api, cb, resources, pc);

        // Transition depth back for next frame's GBuffer pass
        if (vizMode == VisualizationMode.Depth)
        {
            var depthBarrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.DepthStencilReadOnlyOptimal,
                NewLayout = ImageLayout.DepthStencilAttachmentOptimal,
                SrcAccessMask = AccessFlags.ShaderReadBit,
                DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit,
                Image = depthImage,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.DepthBit,
                    BaseMipLevel = 0, LevelCount = 1,
                    BaseArrayLayer = 0, LayerCount = 1,
                },
            };
            api.CmdPipelineBarrier(cb,
                PipelineStageFlags.FragmentShaderBit,
                PipelineStageFlags.EarlyFragmentTestsBit,
                0, 0, null, 0, null, 1, &depthBarrier);
        }
    }

    // ─── ImGui overlay ───────────────────────────────────────────────

    void RecordImGuiPass(Vk api, CommandBuffer cb, uint imageIndex, float dt)
    {
        imgui.NewFrame(window.Width, window.Height, dt);

        // App shell: menu bar (File / Demo) lets the user switch demos or exit.
        // No "View" menu here — GBufferDemo has its own fixed debug layout.
        var appMessages = new List<AppUiMsg>();
        AppMenuBar.Draw(app, appMessages.Add, includeViewMenu: false);
        ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        // G-Buffer visualization selector (4 modes only — no Final or HDR)
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 60), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("G-Buffer Visualization"))
        {
            int currentIndex = Array.IndexOf(Modes, vizMode);
            if (currentIndex < 0) currentIndex = 0;
            if (ImGui.Combo("Buffer", ref currentIndex, ModeNames, ModeNames.Length))
                vizMode = Modes[currentIndex];
        }
        ImGui.End();

        // Camera controls
        FreeCameraDebugMenu.Draw(cameraState, msg =>
        {
            if (msg is UiMsg.UpdateCamera u) cameraState = u.Camera;
        });
        camera = FreeCameraController.ToCamera(cameraState,
            (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height);

        // Frame time
        ImGui.SetNextWindowPos(new Vector2(10, 80), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(200, 50), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Frame"))
            ImGui.Text($"{dt * 1000:F1} ms ({1.0f / dt:F0} FPS)");
        ImGui.End();

        app = AppUiUpdate.ApplyAll(app, appMessages);

        imgui.RecordCommands(api, cb, overlayRenderPass,
            overlayFramebuffers[imageIndex], gpu.SwapchainExtent);
    }

    // ─── Resource management ─────────────────────────────────────────

    void CreateTransientResources()
    {
        var extent = gpu.SwapchainExtent;
        uint w = extent.Width, h = extent.Height;

        // GBuffer images
        (gbufferPosImage, gbufferPosAlloc, gbufferPosView) =
            VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferPositionFormat, w, h);
        (gbufferNormImage, gbufferNormAlloc, gbufferNormView) =
            VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferNormalFormat, w, h);
        (gbufferAlbImage, gbufferAlbAlloc, gbufferAlbView) =
            VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferAlbedoFormat, w, h);

        // Depth (samplable so we can visualize it)
        (depthImage, depthAlloc, depthView) =
            VulkanImage.CreateDepthImage(gpu, w, h, gpu.Capabilities.DepthFormat, samplable: true);

        // Framebuffers
        gbufferFramebuffer = VulkanPipeline.CreateGBufferFramebuffer(
            gpu, gbufferRenderPass, gbufferPosView, gbufferNormView, gbufferAlbView, depthView, w, h);
        swapchainFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, swapchainRenderPass);
        overlayFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, overlayRenderPass);

        // Descriptor sets — one per buffer × frames-in-flight
        uint frames = (uint)GpuState.MaxFramesInFlight;
        debugVizDescPool = VulkanDescriptors.CreatePool(gpu, frames * 4, 1);
        debugVizPositionSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferPosView, sampler);
        debugVizNormalSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferNormView, sampler);
        debugVizAlbedoSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferAlbView, sampler);
        debugVizDepthSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, depthView, sampler,
            ImageLayout.DepthStencilReadOnlyOptimal);

        camera = FreeCameraController.ToCamera(cameraState, (float)w / h);
    }

    unsafe void DestroyTransientResources()
    {
        VulkanPipeline.DestroyFramebuffers(gpu, overlayFramebuffers);
        VulkanPipeline.DestroyFramebuffers(gpu, swapchainFramebuffers);
        vk.DestroyDescriptorPool(gpu.Device, debugVizDescPool, null);
        vk.DestroyFramebuffer(gpu.Device, gbufferFramebuffer, null);
        VulkanImage.DestroyOffscreen(gpu, depthImage, depthAlloc, depthView);
        VulkanImage.DestroyOffscreen(gpu, gbufferAlbImage, gbufferAlbAlloc, gbufferAlbView);
        VulkanImage.DestroyOffscreen(gpu, gbufferNormImage, gbufferNormAlloc, gbufferNormView);
        VulkanImage.DestroyOffscreen(gpu, gbufferPosImage, gbufferPosAlloc, gbufferPosView);
    }

    void RecreateSwapchainResources()
    {
        vk.DeviceWaitIdle(gpu.Device);
        DestroyTransientResources();
        VulkanDevice.DestroyRenderFinishedSemaphores(gpu);
        VulkanSwapchain.Recreate(gpu, (uint)window.Width, (uint)window.Height);
        VulkanDevice.CreateRenderFinishedSemaphores(gpu);
        CreateTransientResources();
    }

    // ─── Cleanup ─────────────────────────────────────────────────────

    public unsafe void Dispose()
    {
        vk.DeviceWaitIdle(gpu.Device);

        imgui.Dispose();
        DestroyTransientResources();

        vk.DestroySampler(gpu.Device, sampler, null);
        vk.DestroyPipeline(gpu.Device, gbufferPipeline, null);
        vk.DestroyPipelineLayout(gpu.Device, gbufferPipelineLayout, null);
        vk.DestroyRenderPass(gpu.Device, gbufferRenderPass, null);
        vk.DestroyPipeline(gpu.Device, debugVizPipeline, null);
        vk.DestroyPipelineLayout(gpu.Device, debugVizPipelineLayout, null);
        vk.DestroyRenderPass(gpu.Device, swapchainRenderPass, null);
        vk.DestroyRenderPass(gpu.Device, overlayRenderPass, null);
        vk.DestroyDescriptorSetLayout(gpu.Device, singleDsLayout, null);

        VulkanBuffer.Destroy(gpu, vertexBuffer, vertexAlloc);
        VulkanBuffer.Destroy(gpu, indexBuffer, indexAlloc);

        gpu.Dispose();
        window.Dispose();
    }
}
