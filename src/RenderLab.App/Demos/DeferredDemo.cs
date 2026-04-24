using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Vulkan;
using RenderLab.Ui.ImGui;
using RenderLab.Gpu;
using RenderLab.Graph;
using RenderLab.Papers;
using RenderLab.Platform.Desktop;
using RenderLab.Scene;
using RenderLab.Ui;
using Buffer = Silk.NET.Vulkan.Buffer;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.App.Demos;

// ─── M3: Deferred Baseline ──────────────────────────────────────────
// Validates: Multiple color attachments, descriptor sets, push constants,
// fullscreen-quad lighting, ImGui integration.
// Pipeline: GBuffer → Lighting → Tonemap → Ui (ImGui overlay in-graph)

public sealed class DeferredDemo : IDemo
{
    const int WindowWidth = 1280;
    const int WindowHeight = 720;
    const float RotateSensitivity = 0.005f;
    const float PanSensitivity = 0.01f;
    const float ZoomSensitivity = 0.3f;

    // ─── Owned resources ─────────────────────────────────────────────
    DesktopWindow window = null!;
    Vk vk = null!;
    GpuState gpu = null!;

    // Mesh
    uint indexCount;
    Buffer vertexBuffer, indexBuffer;
    Allocation vertexAlloc, indexAlloc;

    // Shaders & render passes
    RenderPass gbufferRenderPass, lightingRenderPass, tonemapRenderPass, overlayRenderPass;
    DescriptorSetLayout gbufferDsLayout, singleDsLayout;
    Pipeline gbufferPipeline, lightingPipeline, tonemapPipeline, debugVizPipeline;
    PipelineLayout gbufferPipelineLayout, lightingPipelineLayout, tonemapPipelineLayout, debugVizPipelineLayout;

    // UI / scene-editing state (Elm-style: pure model, view emits messages, reducer folds)
    UiModel ui = UiModel.Default;
    AppUiModel app = AppUiModel.Default(DemoId.Deferred);
    UiIntent lastIntent = UiIntent.None;

    // Derived per frame from ui.Camera + swapchain aspect
    Camera camera = null!;

    // Transient resources (recreated on resize)
    Sampler sampler;
    Image gbufferPosImage, gbufferNormImage, gbufferAlbImage, depthImage, hdrImage;
    Allocation gbufferPosAlloc, gbufferNormAlloc, gbufferAlbAlloc, depthAlloc, hdrAlloc;
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

            // Poll input — forward to the camera only if the previous frame's UI
            // didn't capture the mouse. ImGui's IO is populated below so this
            // frame's widgets see the same snapshot.
            var input = window.PollInput();
            var keyboard = window.PollKeyboard();

            if (!lastIntent.WantCaptureMouse)
            {
                var cameraInput = new CameraInput(
                    YawDelta: input.LeftButtonDown ? -input.MouseDelta.X * RotateSensitivity : 0,
                    PitchDelta: input.LeftButtonDown ? -input.MouseDelta.Y * RotateSensitivity : 0,
                    MoveDelta: new Vector3(
                        input.MiddleButtonDown ? -input.MouseDelta.X * PanSensitivity : 0,
                        input.MiddleButtonDown ?  input.MouseDelta.Y * PanSensitivity : 0,
                        input.ScrollDelta * ZoomSensitivity));

                ui = ui with { Camera = FreeCameraController.Update(ui.Camera, cameraInput) };
                camera = FreeCameraController.ToCamera(ui.Camera, (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height);
            }

            // Feed mouse state to ImGui so it knows what's hovered/clicked
            var io = ImGui.GetIO();
            io.MousePos = input.MousePosition;
            io.MouseDown[0] = input.LeftButtonDown;
            io.MouseDown[1] = input.RightButtonDown;
            io.MouseDown[2] = input.MiddleButtonDown;
            io.MouseWheel = input.ScrollDelta;

            // Feed keyboard state to ImGui so text input and shortcuts work
            foreach (var c in keyboard.TypedChars)
                io.AddInputCharacter(c);
            foreach (var (key, down) in keyboard.KeyEvents)
            {
                var imKey = SilkKeyToImGui(key);
                if (imKey != ImGuiKey.None)
                    io.AddKeyEvent(imKey, down);
            }

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
                ["Ui"] = (api, cb) => RecordImGuiPass(api, cb, imageIndex, deltaTime),
            };

            // Execute compiled render graph (Ui is the final pass)
            VulkanGraphExecutor.Execute(gpu, cmd, resolvedPasses, passRecorders, resourceImages);

            if (!VulkanFrame.EndFrame(gpu, imageIndex))
                RecreateSwapchainResources();
        }

        return null;
    }

    void Init()
    {
        // ─── Load mesh ───────────────────────────────────────────────
        var mesh = ObjLoader.CreateSphere();
        indexCount = (uint)mesh.Indices.Length;

        Console.WriteLine($"RenderLab M3 — Deferred Baseline");
        Console.WriteLine($"  Mesh: {mesh.Vertices.Length} vertices, {mesh.Indices.Length / 3} triangles");

        // ─── Platform + GPU init ─────────────────────────────────────
        window = DesktopWindow.Create("RenderLab — M3 Deferred", WindowWidth, WindowHeight);
        vk = Vk.GetApi();
        gpu = VulkanDevice.Create(vk, window.GetRequiredVulkanExtensions(),
            instance => window.CreateVulkanSurface(instance));

        // ─── Upload mesh to GPU ──────────────────────────────────────
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

        // ─── Camera (derived from UiModel.Default) ───────────────────
        camera = FreeCameraController.ToCamera(ui.Camera, (float)WindowWidth / WindowHeight);

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
                Outputs: [new PassOutput(backbuffer, ResourceUsage.Present)]),
            // Ui reads the already-presentable backbuffer and draws the overlay on top.
            // The overlay render pass (PresentSrcKhr → PresentSrcKhr with LoadOp.Load) owns
            // its own subpass dependency for write-after-write sync with Tonemap, so no
            // external barrier is needed — usage stays Present across the boundary.
            new RenderPassDeclaration("Ui",
                Inputs: [new PassInput(backbuffer, ResourceUsage.Present)],
                Outputs: [])
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
        var pc = new GBufferPushConstants
        {
            Model = ui.MeshTransform.Matrix,
            ViewProj = camera.ViewProjectionMatrix,
            Albedo = ui.Material.Albedo,
            SpecularStrength = ui.Material.SpecularStrength,
            Shininess = ui.Material.Shininess,
        };
        api.CmdPushConstants(cb, gbufferPipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
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

        var pc = DeferredLighting.BuildPushConstants(camera, ui.KeyLight, ui.Shading, ui.LightingOnly);
        DeferredLighting.Record(api, cb, resources, pc);

        timestamps.EndPass(api, cb);
    }

    unsafe void RecordTonemapPass(Vk api, CommandBuffer cb, uint imageIndex)
    {
        timestamps.BeginPass(api, cb, "Tonemap");

        // Transition depth image for sampling when in Depth viz mode
        if (ui.Viz == VisualizationMode.Depth)
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

        if (ui.Viz == VisualizationMode.Final)
        {
            api.CmdBindPipeline(cb, PipelineBindPoint.Graphics, tonemapPipeline);
            var ds = tonemapDescSets[gpu.CurrentFrame];
            api.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, tonemapPipelineLayout, 0, 1, &ds, 0, null);
        }
        else
        {
            api.CmdBindPipeline(cb, PipelineBindPoint.Graphics, debugVizPipeline);

            var ds = ui.Viz switch
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
                Mode = ui.Viz == VisualizationMode.Depth ? 1 : 0,
                NearPlane = camera.NearPlane,
                FarPlane = camera.FarPlane,
            };
            api.CmdPushConstants(cb, debugVizPipelineLayout, ShaderStageFlags.FragmentBit,
                0, (uint)Marshal.SizeOf<DebugVizPushConstants>(), &pc);
        }

        api.CmdDraw(cb, 3, 1, 0, 0);

        api.CmdEndRenderPass(cb);

        // Transition depth back for next frame's GBuffer pass
        if (ui.Viz == VisualizationMode.Depth)
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

        // Materialize the read-only timestamp spans once per frame so the pure
        // view doesn't need to know about GpuTimestamps.
        var stats = new FrameStats(
            DeltaSeconds: dt,
            TimestampLabels: timestamps.Labels.ToArray(),
            TimestampMillis: timestamps.TimingsMs.ToArray(),
            ResolvedPasses: resolvedPasses);

        var viewResult = UiView.Draw(app, ui, stats);
        ui = UiUpdate.ApplyAll(ui, viewResult.Messages);
        app = AppUiUpdate.ApplyAll(app, viewResult.AppMessages);
        lastIntent = viewResult.Intent;

        // UiView may have edited the camera; rebuild the derived Camera before
        // next frame's recorders run. Aspect uses the live swapchain extent.
        camera = FreeCameraController.ToCamera(
            ui.Camera,
            (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height);

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

        // Depth (samplable for debug visualization)
        (depthImage, depthAlloc, depthView) = VulkanImage.CreateDepthImage(gpu, w, h, gpu.Capabilities.DepthFormat, samplable: true);

        // HDR lighting output
        (hdrImage, hdrAlloc, hdrView) =
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

        camera = FreeCameraController.ToCamera(ui.Camera, (float)w / h);
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
        VulkanImage.DestroyOffscreen(gpu, hdrImage, hdrAlloc, hdrView);
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

    // ─── Input mapping ───────────────────────────────────────────────

    private static ImGuiKey SilkKeyToImGui(Key key) => key switch
    {
        Key.Tab           => ImGuiKey.Tab,
        Key.Left          => ImGuiKey.LeftArrow,
        Key.Right         => ImGuiKey.RightArrow,
        Key.Up            => ImGuiKey.UpArrow,
        Key.Down          => ImGuiKey.DownArrow,
        Key.PageUp        => ImGuiKey.PageUp,
        Key.PageDown      => ImGuiKey.PageDown,
        Key.Home          => ImGuiKey.Home,
        Key.End           => ImGuiKey.End,
        Key.Insert        => ImGuiKey.Insert,
        Key.Delete        => ImGuiKey.Delete,
        Key.Backspace     => ImGuiKey.Backspace,
        Key.Space         => ImGuiKey.Space,
        Key.Enter         => ImGuiKey.Enter,
        Key.Escape        => ImGuiKey.Escape,
        Key.ControlLeft   => ImGuiKey.LeftCtrl,
        Key.ControlRight  => ImGuiKey.RightCtrl,
        Key.ShiftLeft     => ImGuiKey.LeftShift,
        Key.ShiftRight    => ImGuiKey.RightShift,
        Key.AltLeft       => ImGuiKey.LeftAlt,
        Key.AltRight      => ImGuiKey.RightAlt,
        Key.SuperLeft     => ImGuiKey.LeftSuper,
        Key.SuperRight    => ImGuiKey.RightSuper,
        Key.A             => ImGuiKey.A,
        Key.B             => ImGuiKey.B,
        Key.C             => ImGuiKey.C,
        Key.D             => ImGuiKey.D,
        Key.E             => ImGuiKey.E,
        Key.F             => ImGuiKey.F,
        Key.G             => ImGuiKey.G,
        Key.H             => ImGuiKey.H,
        Key.I             => ImGuiKey.I,
        Key.J             => ImGuiKey.J,
        Key.K             => ImGuiKey.K,
        Key.L             => ImGuiKey.L,
        Key.M             => ImGuiKey.M,
        Key.N             => ImGuiKey.N,
        Key.O             => ImGuiKey.O,
        Key.P             => ImGuiKey.P,
        Key.Q             => ImGuiKey.Q,
        Key.R             => ImGuiKey.R,
        Key.S             => ImGuiKey.S,
        Key.T             => ImGuiKey.T,
        Key.U             => ImGuiKey.U,
        Key.V             => ImGuiKey.V,
        Key.W             => ImGuiKey.W,
        Key.X             => ImGuiKey.X,
        Key.Y             => ImGuiKey.Y,
        Key.Z             => ImGuiKey.Z,
        _                 => ImGuiKey.None,
    };

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

        VulkanBuffer.Destroy(gpu, vertexBuffer, vertexAlloc);
        VulkanBuffer.Destroy(gpu, indexBuffer, indexAlloc);

        gpu.Dispose();
        window.Dispose();
    }
}
