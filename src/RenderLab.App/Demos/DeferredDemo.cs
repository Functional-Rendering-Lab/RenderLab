using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.Vulkan;
using RenderLab.Debug;
using RenderLab.Gpu;
using RenderLab.Graph;
using RenderLab.Papers;
using RenderLab.Platform.Desktop;
using RenderLab.Scene;
using Buffer = Silk.NET.Vulkan.Buffer;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.App.Demos;

// ─── M3: Deferred Baseline ──────────────────────────────────────────
// Validates: Multiple color attachments, descriptor sets, push constants,
// fullscreen-quad lighting, ImGui integration.
// Pipeline: GBuffer → Lighting → Tonemap → ImGui overlay

public sealed class DeferredDemo : IDemo
{
    const int WindowWidth = 1280;
    const int WindowHeight = 720;
    const float RotateSensitivity = 0.005f;
    const float PanSensitivity = 0.005f;
    const float ZoomSensitivity = 0.3f;

    // ─── Owned resources ─────────────────────────────────────────────
    DesktopWindow window = null!;
    Vk vk = null!;
    GpuState gpu = null!;

    // Mesh
    uint indexCount;
    Buffer vertexBuffer, indexBuffer;
    DeviceMemory vertexMemory, indexMemory;

    // Shaders & render passes
    RenderPass gbufferRenderPass, lightingRenderPass, tonemapRenderPass, overlayRenderPass;
    DescriptorSetLayout gbufferDsLayout, singleDsLayout;
    Pipeline gbufferPipeline, lightingPipeline, tonemapPipeline, debugVizPipeline;
    PipelineLayout gbufferPipelineLayout, lightingPipelineLayout, tonemapPipelineLayout, debugVizPipelineLayout;

    // Camera & scene
    OrbitState orbitState;
    Camera camera = null!;
    PointLight keyLight = null!;
    VisualizationMode vizMode = VisualizationMode.Final;

    // Transient resources (recreated on resize)
    Sampler sampler;
    Image gbufferPosImage, gbufferNormImage, gbufferAlbImage, depthImage, hdrImage;
    DeviceMemory gbufferPosMemory, gbufferNormMemory, gbufferAlbMemory, depthMemory, hdrMemory;
    ImageView gbufferPosView, gbufferNormView, gbufferAlbView, depthView, hdrView;
    Framebuffer gbufferFramebuffer, lightingFramebuffer;
    Framebuffer[] swapchainFramebuffers = [];
    Framebuffer[] overlayFramebuffers = [];
    DescriptorPool gbufferDescPool, tonemapDescPool, debugVizDescPool;
    DescriptorSet[] gbufferDescSets = [];
    DescriptorSet[] tonemapDescSets = [];
    DescriptorSet[] debugVizPositionSets = [], debugVizNormalSets = [], debugVizAlbedoSets = [];
    DescriptorSet[] debugVizDepthSets = [], debugVizHdrSets = [];

    // ImGui & timestamps
    VulkanImGui imgui = null!;
    GpuTimestamps timestamps = null!;

    // Render graph
    ImmutableArray<ResolvedPass> resolvedPasses;
    ResourceName gPosition, gNormal, gAlbedo, hdrColor, backbuffer;

    public void Run()
    {
        Init();

        var frameTimer = System.Diagnostics.Stopwatch.StartNew();
        double lastFrameTime = 0;

        while (!window.IsClosing)
        {
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

            // Poll input — only feed to camera if ImGui doesn't want the mouse
            var input = window.PollInput();
            var io = ImGui.GetIO();

            if (!io.WantCaptureMouse)
            {
                var cameraInput = new CameraInput(
                    YawDelta: input.LeftButtonDown ? -input.MouseDelta.X * RotateSensitivity : 0,
                    PitchDelta: input.LeftButtonDown ? input.MouseDelta.Y * RotateSensitivity : 0,
                    ZoomDelta: input.ScrollDelta * ZoomSensitivity,
                    PanDelta: input.MiddleButtonDown
                        ? new Vector3(-input.MouseDelta.X * PanSensitivity * orbitState.Distance,
                                      input.MouseDelta.Y * PanSensitivity * orbitState.Distance, 0)
                        : Vector3.Zero);

                orbitState = OrbitCameraController.Update(orbitState, cameraInput);
                camera = OrbitCameraController.ToCamera(orbitState, (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height);
            }

            // Feed mouse state to ImGui so it knows what's hovered/clicked
            io.MousePos = input.MousePosition;
            io.MouseDown[0] = input.LeftButtonDown;
            io.MouseDown[1] = input.RightButtonDown;
            io.MouseDown[2] = input.MiddleButtonDown;
            io.MouseWheel = input.ScrollDelta;

            // Read previous frame's GPU timestamps
            timestamps.ReadResults();

            if (!VulkanFrame.BeginFrame(gpu, out var imageIndex))
            {
                RecreateSwapchainResources();
                continue;
            }

            var cmd = gpu.CommandBuffers[gpu.CurrentFrame];

            // Reset timestamp queries
            timestamps.Reset(vk, cmd);

            // Build resource map for graph executor barriers
            var resourceImages = new Dictionary<ResourceName, Image>
            {
                [gPosition] = gbufferPosImage,
                [gNormal] = gbufferNormImage,
                [gAlbedo] = gbufferAlbImage,
                [hdrColor] = hdrImage,
                [backbuffer] = gpu.SwapchainImages[imageIndex],
            };

            // Build pass recorders
            var passRecorders = new Dictionary<string, Action<Vk, CommandBuffer>>
            {
                ["GBuffer"] = (api, cb) => RecordGBufferPass(api, cb),
                ["Lighting"] = (api, cb) => RecordLightingPass(api, cb),
                ["Tonemap"] = (api, cb) => RecordTonemapPass(api, cb, imageIndex),
            };

            // Execute compiled render graph
            VulkanGraphExecutor.Execute(gpu, cmd, resolvedPasses, passRecorders, resourceImages);

            // ImGui overlay (outside render graph — always last)
            RecordImGuiPass(vk, cmd, imageIndex, deltaTime);

            if (!VulkanFrame.EndFrame(gpu, imageIndex))
                RecreateSwapchainResources();
        }
    }

    void Init()
    {
        // ─── Load mesh ───────────────────────────────────────────────
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
        var objPath = Path.Combine(assetsDir, "suzanne.obj");
        var mesh = File.Exists(objPath) ? ObjLoader.Load(objPath) : ObjLoader.CreateCube();
        indexCount = (uint)mesh.Indices.Length;

        Console.WriteLine($"RenderLab M3 — Deferred Baseline");
        Console.WriteLine($"  Mesh: {mesh.Vertices.Length} vertices, {mesh.Indices.Length / 3} triangles");

        // ─── Platform + GPU init ─────────────────────────────────────
        window = DesktopWindow.Create("RenderLab — M3 Deferred", WindowWidth, WindowHeight);
        vk = Vk.GetApi();
        gpu = VulkanDevice.Create(vk, window.GetRequiredVulkanExtensions(),
            instance => window.CreateVulkanSurface(instance));

        // ─── Upload mesh to GPU ──────────────────────────────────────
        (vertexBuffer, vertexMemory) = VulkanBuffer.Create<Vertex3D>(gpu, BufferUsageFlags.VertexBufferBit,
            mesh.Vertices);
        (indexBuffer, indexMemory) = VulkanBuffer.Create<uint>(gpu, BufferUsageFlags.IndexBufferBit,
            mesh.Indices);

        // ─── Shaders ─────────────────────────────────────────────────
        var shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
        byte[] LoadSpv(string name) => File.ReadAllBytes(Path.Combine(shaderDir, name));

        var gbufferVertModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.vert.spv"));
        var gbufferFragModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.frag.spv"));
        var fsVertModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("fullscreen.vert.spv"));
        var lightingFragModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("lighting.frag.spv"));
        var tonemapFragModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("tonemap.frag.spv"));

        // ─── Render passes ───────────────────────────────────────────
        gbufferRenderPass = VulkanPipeline.CreateGBufferRenderPass(gpu);
        lightingRenderPass = VulkanPipeline.CreateOffscreenRenderPass(gpu, VulkanPipeline.HdrFormat);
        tonemapRenderPass = VulkanPipeline.CreateRenderPass(gpu);
        overlayRenderPass = VulkanPipeline.CreateOverlayRenderPass(gpu);

        // ─── Descriptor set layouts ──────────────────────────────────
        gbufferDsLayout = VulkanDescriptors.CreateGBufferSamplerLayout(gpu);
        singleDsLayout = VulkanDescriptors.CreateSamplerLayout(gpu);

        // ─── Pipelines ───────────────────────────────────────────────
        gbufferPipeline = VulkanPipeline.CreateGBufferPipeline(
            gpu, gbufferRenderPass, gbufferVertModule, gbufferFragModule,
            Vertex3D.BindingDescription, Vertex3D.AttributeDescriptions,
            (uint)Marshal.SizeOf<GBufferPushConstants>(),
            out gbufferPipelineLayout);

        lightingPipeline = VulkanPipeline.CreateFullscreenPipeline(
            gpu, lightingRenderPass, gbufferDsLayout, fsVertModule, lightingFragModule,
            (uint)Marshal.SizeOf<LightingPushConstants>(), ShaderStageFlags.FragmentBit,
            out lightingPipelineLayout);

        tonemapPipeline = VulkanPipeline.CreateFullscreenPipeline(
            gpu, tonemapRenderPass, singleDsLayout, fsVertModule, tonemapFragModule,
            0, ShaderStageFlags.None,
            out tonemapPipelineLayout);

        // Debug visualization pipeline
        var debugVizFragModule = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("debugviz.frag.spv"));
        debugVizPipeline = VulkanPipeline.CreateFullscreenPipeline(
            gpu, tonemapRenderPass, singleDsLayout, fsVertModule, debugVizFragModule,
            (uint)Marshal.SizeOf<DebugVizPushConstants>(), ShaderStageFlags.FragmentBit,
            out debugVizPipelineLayout);

        // Destroy shader modules after pipeline creation
        unsafe
        {
            vk.DestroyShaderModule(gpu.Device, gbufferVertModule, null);
            vk.DestroyShaderModule(gpu.Device, gbufferFragModule, null);
            vk.DestroyShaderModule(gpu.Device, fsVertModule, null);
            vk.DestroyShaderModule(gpu.Device, lightingFragModule, null);
            vk.DestroyShaderModule(gpu.Device, tonemapFragModule, null);
            vk.DestroyShaderModule(gpu.Device, debugVizFragModule, null);
        }

        // ─── Camera ──────────────────────────────────────────────────
        orbitState = OrbitCameraController.CreateDefault();
        camera = OrbitCameraController.ToCamera(orbitState, (float)WindowWidth / WindowHeight);

        keyLight = new PointLight(
            Position: new Vector3(2, 3, 2),
            Color: new Vector3(1f, 0.95f, 0.9f),
            Intensity: 5f);

        // ─── Transient resources ─────────────────────────────────────
        sampler = VulkanImage.CreateSampler(gpu);
        CreateTransientResources();

        // ─── ImGui + GPU timestamps ──────────────────────────────────
        imgui = VulkanImGui.Create(gpu, overlayRenderPass);
        timestamps = GpuTimestamps.Create(gpu, 4);

        // ─── Compile render graph (pure) ─────────────────────────────
        gPosition = new ResourceName("GBuffer.Position");
        gNormal = new ResourceName("GBuffer.Normal");
        gAlbedo = new ResourceName("GBuffer.Albedo");
        hdrColor = new ResourceName("HDR");
        backbuffer = new ResourceName("Backbuffer");

        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("GBuffer",
                Inputs: [],
                Outputs: [
                    new PassOutput(gPosition, ResourceUsage.ColorAttachmentWrite),
                    new PassOutput(gNormal, ResourceUsage.ColorAttachmentWrite),
                    new PassOutput(gAlbedo, ResourceUsage.ColorAttachmentWrite),
                ]),
            new RenderPassDeclaration("Lighting",
                Inputs: [
                    new PassInput(gPosition, ResourceUsage.ShaderRead),
                    new PassInput(gNormal, ResourceUsage.ShaderRead),
                    new PassInput(gAlbedo, ResourceUsage.ShaderRead),
                ],
                Outputs: [new PassOutput(hdrColor, ResourceUsage.ColorAttachmentWrite)]),
            new RenderPassDeclaration("Tonemap",
                Inputs: [new PassInput(hdrColor, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(backbuffer, ResourceUsage.Present)])
        );

        resolvedPasses = RenderGraphCompiler.Compile(passes);

        Console.WriteLine($"  Swapchain: {gpu.SwapchainExtent.Width}x{gpu.SwapchainExtent.Height}");
        Console.WriteLine($"  Passes: {string.Join(" -> ", resolvedPasses.Select(p => p.Declaration.Name))}");
        Console.WriteLine($"  Barriers: {resolvedPasses.Sum(p => p.BarriersBefore.Length)}");
    }

    // ─── Pass recording ──────────────────────────────────────────────

    unsafe void RecordGBufferPass(Vk api, CommandBuffer cb)
    {
        timestamps.BeginPass(api, cb, "GBuffer");

        var clearValues = stackalloc ClearValue[4];
        clearValues[0] = new ClearValue(new ClearColorValue(0, 0, 0, 0));
        clearValues[1] = new ClearValue(new ClearColorValue(0, 0, 0, 0));
        clearValues[2] = new ClearValue(new ClearColorValue(0, 0, 0, 0));
        clearValues[3] = new ClearValue(depthStencil: new ClearDepthStencilValue(1.0f, 0));

        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = gbufferRenderPass,
            Framebuffer = gbufferFramebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent),
            ClearValueCount = 4,
            PClearValues = clearValues,
        };

        api.CmdBeginRenderPass(cb, &renderPassBegin, SubpassContents.Inline);
        api.CmdBindPipeline(cb, PipelineBindPoint.Graphics, gbufferPipeline);

        var viewport = new Viewport(0, 0, gpu.SwapchainExtent.Width, gpu.SwapchainExtent.Height, 0, 1);
        api.CmdSetViewport(cb, 0, 1, &viewport);

        var scissor = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent);
        api.CmdSetScissor(cb, 0, 1, &scissor);

        // Push constants
        var model = Matrix4x4.Identity;
        var pc = new GBufferPushConstants
        {
            Model = model,
            ViewProj = camera.ViewProjectionMatrix,
        };
        api.CmdPushConstants(cb, gbufferPipelineLayout, ShaderStageFlags.VertexBit,
            0, (uint)Marshal.SizeOf<GBufferPushConstants>(), &pc);

        var vb = vertexBuffer;
        ulong offset = 0;
        api.CmdBindVertexBuffers(cb, 0, 1, &vb, &offset);
        api.CmdBindIndexBuffer(cb, indexBuffer, 0, IndexType.Uint32);
        api.CmdDrawIndexed(cb, indexCount, 1, 0, 0, 0);

        api.CmdEndRenderPass(cb);

        timestamps.EndPass(api, cb);
    }

    void RecordLightingPass(Vk api, CommandBuffer cb)
    {
        timestamps.BeginPass(api, cb, "Lighting");

        var resources = new LightingPassResources(
            RenderPass: lightingRenderPass,
            Framebuffer: lightingFramebuffer,
            Pipeline: lightingPipeline,
            PipelineLayout: lightingPipelineLayout,
            GBufferDescriptorSet: gbufferDescSets[gpu.CurrentFrame],
            Extent: gpu.SwapchainExtent);

        var pc = DeferredLighting.BuildPushConstants(camera, keyLight);
        DeferredLighting.Record(api, cb, resources, pc);

        timestamps.EndPass(api, cb);
    }

    unsafe void RecordTonemapPass(Vk api, CommandBuffer cb, uint imageIndex)
    {
        timestamps.BeginPass(api, cb, "Tonemap");

        // Transition depth image for sampling when in Depth viz mode
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

        var clearValue = new ClearValue(new ClearColorValue(0, 0, 0, 1));

        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = tonemapRenderPass,
            Framebuffer = swapchainFramebuffers[imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent),
            ClearValueCount = 1,
            PClearValues = &clearValue,
        };

        api.CmdBeginRenderPass(cb, &renderPassBegin, SubpassContents.Inline);

        var viewport = new Viewport(0, 0, gpu.SwapchainExtent.Width, gpu.SwapchainExtent.Height, 0, 1);
        api.CmdSetViewport(cb, 0, 1, &viewport);

        var scissor = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent);
        api.CmdSetScissor(cb, 0, 1, &scissor);

        if (vizMode == VisualizationMode.Final)
        {
            api.CmdBindPipeline(cb, PipelineBindPoint.Graphics, tonemapPipeline);
            var ds = tonemapDescSets[gpu.CurrentFrame];
            api.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, tonemapPipelineLayout, 0, 1, &ds, 0, null);
        }
        else
        {
            api.CmdBindPipeline(cb, PipelineBindPoint.Graphics, debugVizPipeline);

            var ds = vizMode switch
            {
                VisualizationMode.Position => debugVizPositionSets[gpu.CurrentFrame],
                VisualizationMode.Normal => debugVizNormalSets[gpu.CurrentFrame],
                VisualizationMode.Albedo => debugVizAlbedoSets[gpu.CurrentFrame],
                VisualizationMode.Depth => debugVizDepthSets[gpu.CurrentFrame],
                VisualizationMode.HDR => debugVizHdrSets[gpu.CurrentFrame],
                _ => tonemapDescSets[gpu.CurrentFrame],
            };
            api.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, debugVizPipelineLayout, 0, 1, &ds, 0, null);

            var pc = new DebugVizPushConstants
            {
                Mode = vizMode == VisualizationMode.Depth ? 1 : 0,
                NearPlane = camera.NearPlane,
                FarPlane = camera.FarPlane,
            };
            api.CmdPushConstants(cb, debugVizPipelineLayout, ShaderStageFlags.FragmentBit,
                0, (uint)Marshal.SizeOf<DebugVizPushConstants>(), &pc);
        }

        api.CmdDraw(cb, 3, 1, 0, 0);

        api.CmdEndRenderPass(cb);

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

        timestamps.EndPass(api, cb);
    }

    void RecordImGuiPass(Vk api, CommandBuffer cb, uint imageIndex, float dt)
    {
        imgui.NewFrame(window.Width, window.Height, dt);

        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 140), ImGuiCond.FirstUseEver);
        ImGui.Begin("GPU Timings");

        var labels = timestamps.Labels;
        var timings = timestamps.TimingsMs;
        float total = 0;
        for (int i = 0; i < timings.Length; i++)
        {
            ImGui.Text($"{labels[i]}: {timings[i]:F3} ms");
            total += (float)timings[i];
        }
        ImGui.Separator();
        ImGui.Text($"Total GPU: {total:F3} ms");
        ImGui.Text($"Frame: {dt * 1000:F1} ms ({1.0f / dt:F0} FPS)");

        ImGui.End();

        ImGui.SetNextWindowPos(new Vector2(10, 370), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 60), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Visualization"))
            vizMode = VisualizationDebugMenu.Draw(vizMode);
        ImGui.End();

        orbitState = OrbitCameraDebugMenu.Draw(orbitState);
        camera = OrbitCameraController.ToCamera(orbitState, (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height);

        keyLight = LightingDebugMenu.Draw(keyLight);

        RenderGraphDebugMenu.Draw(resolvedPasses);

        imgui.RecordCommands(api, cb, overlayRenderPass,
            overlayFramebuffers[imageIndex], gpu.SwapchainExtent);
    }

    // ─── Resource management ─────────────────────────────────────────

    void CreateTransientResources()
    {
        var extent = gpu.SwapchainExtent;
        uint w = extent.Width, h = extent.Height;

        // GBuffer images
        (gbufferPosImage, gbufferPosMemory, gbufferPosView) =
            VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferPositionFormat, w, h);
        (gbufferNormImage, gbufferNormMemory, gbufferNormView) =
            VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferNormalFormat, w, h);
        (gbufferAlbImage, gbufferAlbMemory, gbufferAlbView) =
            VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferAlbedoFormat, w, h);

        // Depth (samplable for debug visualization)
        (depthImage, depthMemory, depthView) = VulkanImage.CreateDepthImage(gpu, w, h, gpu.Capabilities.DepthFormat, samplable: true);

        // HDR lighting output
        (hdrImage, hdrMemory, hdrView) =
            VulkanImage.CreateOffscreen(gpu, VulkanPipeline.HdrFormat, w, h);

        // GBuffer framebuffer (3 color + 1 depth)
        gbufferFramebuffer = VulkanPipeline.CreateGBufferFramebuffer(
            gpu, gbufferRenderPass, gbufferPosView, gbufferNormView, gbufferAlbView, depthView, w, h);

        // Lighting framebuffer (1 color: HDR)
        lightingFramebuffer = VulkanPipeline.CreateOffscreenFramebuffer(
            gpu, lightingRenderPass, hdrView, w, h);

        // Swapchain framebuffers for tonemap pass
        swapchainFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, tonemapRenderPass);

        // Overlay framebuffers (LoadOp.Load — renders on top of tonemap output)
        overlayFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, overlayRenderPass);

        // Descriptor pools + sets
        uint frames = (uint)GpuState.MaxFramesInFlight;

        gbufferDescPool = VulkanDescriptors.CreatePool(gpu, frames, 3);
        gbufferDescSets = VulkanDescriptors.AllocateGBufferSets(
            gpu, gbufferDescPool, gbufferDsLayout, frames,
            gbufferPosView, gbufferNormView, gbufferAlbView, sampler);

        tonemapDescPool = VulkanDescriptors.CreatePool(gpu, frames, 1);
        tonemapDescSets = VulkanDescriptors.AllocateSets(
            gpu, tonemapDescPool, singleDsLayout, frames, hdrView, sampler);

        // Debug viz descriptor sets — 5 buffers × frames-in-flight
        debugVizDescPool = VulkanDescriptors.CreatePool(gpu, frames * 5, 1);
        debugVizPositionSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferPosView, sampler);
        debugVizNormalSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferNormView, sampler);
        debugVizAlbedoSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferAlbView, sampler);
        debugVizDepthSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, depthView, sampler,
            ImageLayout.DepthStencilReadOnlyOptimal);
        debugVizHdrSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, hdrView, sampler);

        camera = OrbitCameraController.ToCamera(orbitState, (float)w / h);
    }

    unsafe void DestroyTransientResources()
    {
        VulkanPipeline.DestroyFramebuffers(gpu, overlayFramebuffers);
        VulkanPipeline.DestroyFramebuffers(gpu, swapchainFramebuffers);
        vk.DestroyDescriptorPool(gpu.Device, debugVizDescPool, null);
        vk.DestroyDescriptorPool(gpu.Device, tonemapDescPool, null);
        vk.DestroyDescriptorPool(gpu.Device, gbufferDescPool, null);
        vk.DestroyFramebuffer(gpu.Device, lightingFramebuffer, null);
        vk.DestroyFramebuffer(gpu.Device, gbufferFramebuffer, null);
        VulkanImage.DestroyOffscreen(gpu, hdrImage, hdrMemory, hdrView);
        VulkanImage.DestroyOffscreen(gpu, depthImage, depthMemory, depthView);
        VulkanImage.DestroyOffscreen(gpu, gbufferAlbImage, gbufferAlbMemory, gbufferAlbView);
        VulkanImage.DestroyOffscreen(gpu, gbufferNormImage, gbufferNormMemory, gbufferNormView);
        VulkanImage.DestroyOffscreen(gpu, gbufferPosImage, gbufferPosMemory, gbufferPosView);
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

        timestamps.Dispose();
        imgui.Dispose();
        DestroyTransientResources();

        vk.DestroySampler(gpu.Device, sampler, null);
        vk.DestroyPipeline(gpu.Device, gbufferPipeline, null);
        vk.DestroyPipelineLayout(gpu.Device, gbufferPipelineLayout, null);
        vk.DestroyRenderPass(gpu.Device, gbufferRenderPass, null);
        vk.DestroyPipeline(gpu.Device, lightingPipeline, null);
        vk.DestroyPipelineLayout(gpu.Device, lightingPipelineLayout, null);
        vk.DestroyRenderPass(gpu.Device, lightingRenderPass, null);
        vk.DestroyPipeline(gpu.Device, tonemapPipeline, null);
        vk.DestroyPipelineLayout(gpu.Device, tonemapPipelineLayout, null);
        vk.DestroyPipeline(gpu.Device, debugVizPipeline, null);
        vk.DestroyPipelineLayout(gpu.Device, debugVizPipelineLayout, null);
        vk.DestroyRenderPass(gpu.Device, tonemapRenderPass, null);
        vk.DestroyRenderPass(gpu.Device, overlayRenderPass, null);
        vk.DestroyDescriptorSetLayout(gpu.Device, gbufferDsLayout, null);
        vk.DestroyDescriptorSetLayout(gpu.Device, singleDsLayout, null);

        VulkanBuffer.Destroy(gpu, vertexBuffer, vertexMemory);
        VulkanBuffer.Destroy(gpu, indexBuffer, indexMemory);

        gpu.Dispose();
        window.Dispose();
    }
}
