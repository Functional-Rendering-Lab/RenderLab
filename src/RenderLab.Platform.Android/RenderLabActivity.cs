using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Silk.NET.Vulkan;
using RenderLab.Gpu;
using RenderLab.Graph;
using RenderLab.Scene;
using Buffer = Silk.NET.Vulkan.Buffer;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;
using Image = Silk.NET.Vulkan.Image;
using ImageView = Silk.NET.Vulkan.ImageView;

namespace RenderLab.Platform.Android;

/// <summary>
/// Main Android Activity hosting a SurfaceView for Vulkan rendering.
/// Phase 2: Full deferred pipeline — GBuffer → Lighting → Tonemap.
/// </summary>
[Activity(
    Label = "RenderLab",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public class RenderLabActivity : Activity, ISurfaceHolderCallback
{
    private const string Tag = "RenderLab";

    private SurfaceView? _surfaceView;
    private AndroidWindow? _window;
    private GpuState? _gpu;
    private Thread? _renderThread;
    private volatile bool _running;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Window?.AddFlags(WindowManagerFlags.Fullscreen);

        _surfaceView = new SurfaceView(this);
        SetContentView(_surfaceView);

        _surfaceView.Holder!.AddCallback(this);

        Log.Info(Tag, "Activity created — waiting for surface");
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        Log.Info(Tag, "Surface created");
    }

    public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
    {
        Log.Info(Tag, $"Surface changed: {width}x{height}");

        var surface = holder.Surface;
        if (surface == null)
        {
            Log.Error(Tag, "Surface is null");
            return;
        }

        var nativeWindow = ANativeWindow_fromSurface(JNIEnv.Handle, surface.Handle);

        if (nativeWindow == 0)
        {
            Log.Error(Tag, "ANativeWindow_fromSurface returned null");
            return;
        }

        if (_window == null)
        {
            _window = new AndroidWindow(nativeWindow, width, height);
            StartRenderThread();
        }
        else
        {
            _window.UpdateSurface(nativeWindow, width, height);
            if (_gpu != null)
                _gpu.FramebufferResized = true;
        }
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        Log.Info(Tag, "Surface destroyed — stopping render");
        StopRenderThread();

        if (_gpu != null)
        {
            _gpu.Vk.DeviceWaitIdle(_gpu.Device);
            _gpu.Dispose();
            _gpu = null;
        }

        _window?.Dispose();
        _window = null;
    }

    protected override void OnDestroy()
    {
        StopRenderThread();
        base.OnDestroy();
    }

    private void StartRenderThread()
    {
        if (_running) return;
        _running = true;
        _renderThread = new Thread(RenderLoop) { Name = "RenderLab-Render", IsBackground = true };
        _renderThread.Start();
    }

    private void StopRenderThread()
    {
        _running = false;
        _window?.RequestClose();
        _renderThread?.Join(2000);
        _renderThread = null;
    }

    /// <summary>
    /// Loads a SPIR-V shader from Android assets (bundled in the APK).
    /// </summary>
    private byte[] LoadShaderAsset(string name)
    {
        using var stream = Assets!.Open($"shaders/{name}");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private void RenderLoop()
    {
        try
        {
            var window = _window!;
            var vk = Vk.GetApi();

            Log.Info(Tag, "Creating Vulkan device (API 1.1)...");

            _gpu = VulkanDevice.Create(
                vk,
                window.GetRequiredVulkanExtensions(),
                instance => window.CreateVulkanSurface(instance),
                vulkanApiVersion: Vk.Version11,
                initialWidth: (uint)window.Width,
                initialHeight: (uint)window.Height);

            var gpu = _gpu;

            Log.Info(Tag, $"Vulkan ready — swapchain {gpu.SwapchainExtent.Width}x{gpu.SwapchainExtent.Height}");
            Log.Info(Tag, $"Device: {gpu.Capabilities.DeviceName}");
            Log.Info(Tag, $"Depth format: {gpu.Capabilities.DepthFormat}, MRT: {gpu.Capabilities.MaxColorAttachments}");

            var depthFormat = gpu.Capabilities.DepthFormat;

            // ─── Load mesh (built-in cube) ──────────────────────────────
            var mesh = ObjLoader.CreateCube();
            uint indexCount = (uint)mesh.Indices.Length;
            Log.Info(Tag, $"Mesh: {mesh.Vertices.Length} verts, {mesh.Indices.Length / 3} tris");

            // ─── Upload mesh to GPU ─────────────────────────────────────
            var (vertexBuffer, vertexMemory) = VulkanBuffer.Create<Vertex3D>(
                gpu, BufferUsageFlags.VertexBufferBit, mesh.Vertices);
            var (indexBuffer, indexMemory) = VulkanBuffer.Create<uint>(
                gpu, BufferUsageFlags.IndexBufferBit, mesh.Indices);

            // ─── Shaders ────────────────────────────────────────────────
            var gbufferVertModule = VulkanPipeline.CreateShaderModule(gpu, LoadShaderAsset("gbuffer.vert.spv"));
            var gbufferFragModule = VulkanPipeline.CreateShaderModule(gpu, LoadShaderAsset("gbuffer.frag.spv"));
            var fsVertModule = VulkanPipeline.CreateShaderModule(gpu, LoadShaderAsset("fullscreen.vert.spv"));
            var lightingFragModule = VulkanPipeline.CreateShaderModule(gpu, LoadShaderAsset("lighting.frag.spv"));
            var tonemapFragModule = VulkanPipeline.CreateShaderModule(gpu, LoadShaderAsset("tonemap.frag.spv"));

            // ─── Render passes ──────────────────────────────────────────
            var gbufferRenderPass = VulkanPipeline.CreateGBufferRenderPass(gpu, depthFormat);
            var lightingRenderPass = VulkanPipeline.CreateOffscreenRenderPass(gpu, VulkanPipeline.HdrFormat);
            var tonemapRenderPass = VulkanPipeline.CreateRenderPass(gpu);

            // ─── Descriptor set layouts ─────────────────────────────────
            var gbufferDsLayout = VulkanDescriptors.CreateGBufferSamplerLayout(gpu);
            var singleDsLayout = VulkanDescriptors.CreateSamplerLayout(gpu);

            // ─── Pipelines ──────────────────────────────────────────────
            var gbufferPipeline = VulkanPipeline.CreateGBufferPipeline(
                gpu, gbufferRenderPass, gbufferVertModule, gbufferFragModule,
                Vertex3D.BindingDescription, Vertex3D.AttributeDescriptions,
                (uint)Marshal.SizeOf<GBufferPushConstants>(),
                out var gbufferPipelineLayout);

            var lightingPipeline = VulkanPipeline.CreateFullscreenPipeline(
                gpu, lightingRenderPass, gbufferDsLayout, fsVertModule, lightingFragModule,
                (uint)Marshal.SizeOf<LightingPushConstants>(), ShaderStageFlags.FragmentBit,
                out var lightingPipelineLayout);

            var tonemapPipeline = VulkanPipeline.CreateFullscreenPipeline(
                gpu, tonemapRenderPass, singleDsLayout, fsVertModule, tonemapFragModule,
                0, ShaderStageFlags.None,
                out var tonemapPipelineLayout);

            // Destroy shader modules after pipeline creation
            unsafe
            {
                vk.DestroyShaderModule(gpu.Device, gbufferVertModule, null);
                vk.DestroyShaderModule(gpu.Device, gbufferFragModule, null);
                vk.DestroyShaderModule(gpu.Device, fsVertModule, null);
                vk.DestroyShaderModule(gpu.Device, lightingFragModule, null);
                vk.DestroyShaderModule(gpu.Device, tonemapFragModule, null);
            }

            // ─── Camera ────────────────────────────────────────────────
            var aspect = (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height;
            var camera = Camera.CreateDefault(aspect);
            float rotationAngle = 0;

            // ─── Transient resources ────────────────────────────────────
            var sampler = VulkanImage.CreateSampler(gpu);

            var gbufferPosImage = default(Image); var gbufferPosMemory = default(DeviceMemory); var gbufferPosView = default(ImageView);
            var gbufferNormImage = default(Image); var gbufferNormMemory = default(DeviceMemory); var gbufferNormView = default(ImageView);
            var gbufferAlbImage = default(Image); var gbufferAlbMemory = default(DeviceMemory); var gbufferAlbView = default(ImageView);
            var depthImage = default(Image); var depthMemory = default(DeviceMemory); var depthView = default(ImageView);
            var hdrImage = default(Image); var hdrMemory = default(DeviceMemory); var hdrView = default(ImageView);
            var gbufferFramebuffer = default(Framebuffer);
            var lightingFramebuffer = default(Framebuffer);
            var swapchainFramebuffers = Array.Empty<Framebuffer>();

            var gbufferDescPool = default(DescriptorPool);
            var gbufferDescSets = Array.Empty<DescriptorSet>();
            var tonemapDescPool = default(DescriptorPool);
            var tonemapDescSets = Array.Empty<DescriptorSet>();

            CreateTransientResources();

            // ─── Compile render graph (pure) ────────────────────────────
            var gPosition = new ResourceName("GBuffer.Position");
            var gNormal = new ResourceName("GBuffer.Normal");
            var gAlbedo = new ResourceName("GBuffer.Albedo");
            var hdrColor = new ResourceName("HDR");
            var backbuffer = new ResourceName("Backbuffer");

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

            var resolvedPasses = RenderGraphCompiler.Compile(passes);

            Log.Info(Tag, $"Passes: {string.Join(" -> ", resolvedPasses.Select(p => p.Declaration.Name))}");
            Log.Info(Tag, $"Barriers: {resolvedPasses.Sum(p => p.BarriersBefore.Length)}");
            Log.Info(Tag, "Entering deferred render loop");

            // ─── Main loop ──────────────────────────────────────────────
            var frameTimer = System.Diagnostics.Stopwatch.StartNew();
            double lastFrameTime = 0;

            while (_running && !window.IsClosing)
            {
                if (window.Width == 0 || window.Height == 0)
                {
                    Thread.Sleep(16);
                    continue;
                }

                if (window.SurfaceInvalidated)
                {
                    window.ClearSurfaceInvalidated();
                    window.ClearResizeFlag();
                    gpu.FramebufferResized = false;
                    RecreateSurface();
                    continue;
                }

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

                rotationAngle += deltaTime * 0.5f;

                if (!VulkanFrame.BeginFrame(gpu, out var imageIndex))
                {
                    RecreateSwapchainResources();
                    continue;
                }

                var cmd = gpu.CommandBuffers[gpu.CurrentFrame];

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

                VulkanGraphExecutor.Execute(gpu, cmd, resolvedPasses, passRecorders, resourceImages);

                if (!VulkanFrame.EndFrame(gpu, imageIndex))
                    RecreateSwapchainResources();
            }

            Log.Info(Tag, "Render loop exited — cleaning up");

            // ─── Cleanup ────────────────────────────────────────────────
            vk.DeviceWaitIdle(gpu.Device);

            DestroyTransientResources();

            unsafe
            {
                vk.DestroySampler(gpu.Device, sampler, null);
                vk.DestroyPipeline(gpu.Device, gbufferPipeline, null);
                vk.DestroyPipelineLayout(gpu.Device, gbufferPipelineLayout, null);
                vk.DestroyRenderPass(gpu.Device, gbufferRenderPass, null);
                vk.DestroyPipeline(gpu.Device, lightingPipeline, null);
                vk.DestroyPipelineLayout(gpu.Device, lightingPipelineLayout, null);
                vk.DestroyRenderPass(gpu.Device, lightingRenderPass, null);
                vk.DestroyPipeline(gpu.Device, tonemapPipeline, null);
                vk.DestroyPipelineLayout(gpu.Device, tonemapPipelineLayout, null);
                vk.DestroyRenderPass(gpu.Device, tonemapRenderPass, null);
                vk.DestroyDescriptorSetLayout(gpu.Device, gbufferDsLayout, null);
                vk.DestroyDescriptorSetLayout(gpu.Device, singleDsLayout, null);
            }
            VulkanBuffer.Destroy(gpu, vertexBuffer, vertexMemory);
            VulkanBuffer.Destroy(gpu, indexBuffer, indexMemory);

            return;

            // ─── Pass recording functions ───────────────────────────────

            unsafe void RecordGBufferPass(Vk api, CommandBuffer cb)
            {
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

                var model = Matrix4x4.CreateRotationY(rotationAngle);
                var pc = new GBufferPushConstants
                {
                    Model = model,
                    ViewProj = camera.ViewProjectionMatrix,
                    Albedo = MaterialParams.Default.Albedo,
                    SpecularStrength = MaterialParams.Default.SpecularStrength,
                    Shininess = MaterialParams.Default.Shininess,
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
            }

            unsafe void RecordLightingPass(Vk api, CommandBuffer cb)
            {
                var clearValue = new ClearValue(new ClearColorValue(0, 0, 0, 1));

                var renderPassBegin = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = lightingRenderPass,
                    Framebuffer = lightingFramebuffer,
                    RenderArea = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent),
                    ClearValueCount = 1,
                    PClearValues = &clearValue,
                };

                api.CmdBeginRenderPass(cb, &renderPassBegin, SubpassContents.Inline);
                api.CmdBindPipeline(cb, PipelineBindPoint.Graphics, lightingPipeline);

                var viewport = new Viewport(0, 0, gpu.SwapchainExtent.Width, gpu.SwapchainExtent.Height, 0, 1);
                api.CmdSetViewport(cb, 0, 1, &viewport);

                var scissor = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent);
                api.CmdSetScissor(cb, 0, 1, &scissor);

                var ds = gbufferDescSets[gpu.CurrentFrame];
                api.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, lightingPipelineLayout, 0, 1, &ds, 0, null);

                var lightPc = new LightingPushConstants
                {
                    CameraPos = new Vector4(camera.Position, 1),
                    LightPos = new Vector4(2, 3, 2, 1),
                    LightColor = new Vector4(1, 0.95f, 0.9f, 5.0f),
                };
                api.CmdPushConstants(cb, lightingPipelineLayout, ShaderStageFlags.FragmentBit,
                    0, (uint)Marshal.SizeOf<LightingPushConstants>(), &lightPc);

                api.CmdDraw(cb, 3, 1, 0, 0);

                api.CmdEndRenderPass(cb);
            }

            unsafe void RecordTonemapPass(Vk api, CommandBuffer cb, uint imgIdx)
            {
                var clearValue = new ClearValue(new ClearColorValue(0, 0, 0, 1));

                var renderPassBegin = new RenderPassBeginInfo
                {
                    SType = StructureType.RenderPassBeginInfo,
                    RenderPass = tonemapRenderPass,
                    Framebuffer = swapchainFramebuffers[imgIdx],
                    RenderArea = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent),
                    ClearValueCount = 1,
                    PClearValues = &clearValue,
                };

                api.CmdBeginRenderPass(cb, &renderPassBegin, SubpassContents.Inline);
                api.CmdBindPipeline(cb, PipelineBindPoint.Graphics, tonemapPipeline);

                var viewport = new Viewport(0, 0, gpu.SwapchainExtent.Width, gpu.SwapchainExtent.Height, 0, 1);
                api.CmdSetViewport(cb, 0, 1, &viewport);

                var scissor = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent);
                api.CmdSetScissor(cb, 0, 1, &scissor);

                var ds = tonemapDescSets[gpu.CurrentFrame];
                api.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, tonemapPipelineLayout, 0, 1, &ds, 0, null);

                api.CmdDraw(cb, 3, 1, 0, 0);

                api.CmdEndRenderPass(cb);
            }

            // ─── Resource management ────────────────────────────────────

            void CreateTransientResources()
            {
                var extent = gpu.SwapchainExtent;
                uint w = extent.Width, h = extent.Height;

                (gbufferPosImage, gbufferPosMemory, gbufferPosView) =
                    VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferPositionFormat, w, h);
                (gbufferNormImage, gbufferNormMemory, gbufferNormView) =
                    VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferNormalFormat, w, h);
                (gbufferAlbImage, gbufferAlbMemory, gbufferAlbView) =
                    VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferAlbedoFormat, w, h);

                (depthImage, depthMemory, depthView) =
                    VulkanImage.CreateDepthImage(gpu, w, h, depthFormat);

                (hdrImage, hdrMemory, hdrView) =
                    VulkanImage.CreateOffscreen(gpu, VulkanPipeline.HdrFormat, w, h);

                gbufferFramebuffer = VulkanPipeline.CreateGBufferFramebuffer(
                    gpu, gbufferRenderPass, gbufferPosView, gbufferNormView, gbufferAlbView, depthView, w, h);

                lightingFramebuffer = VulkanPipeline.CreateOffscreenFramebuffer(
                    gpu, lightingRenderPass, hdrView, w, h);

                swapchainFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, tonemapRenderPass);

                uint frames = (uint)GpuState.MaxFramesInFlight;

                gbufferDescPool = VulkanDescriptors.CreatePool(gpu, frames, 3);
                gbufferDescSets = VulkanDescriptors.AllocateGBufferSets(
                    gpu, gbufferDescPool, gbufferDsLayout, frames,
                    gbufferPosView, gbufferNormView, gbufferAlbView, sampler);

                tonemapDescPool = VulkanDescriptors.CreatePool(gpu, frames, 1);
                tonemapDescSets = VulkanDescriptors.AllocateSets(
                    gpu, tonemapDescPool, singleDsLayout, frames, hdrView, sampler);

                camera = camera.WithAspect((float)w / h);
            }

            unsafe void DestroyTransientResources()
            {
                VulkanPipeline.DestroyFramebuffers(gpu, swapchainFramebuffers);
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

            unsafe void RecreateSurface()
            {
                vk.DeviceWaitIdle(gpu.Device);
                DestroyTransientResources();
                VulkanDevice.DestroyRenderFinishedSemaphores(gpu);
                VulkanSwapchain.Destroy(gpu);
                gpu.KhrSurface.DestroySurface(gpu.Instance, gpu.Surface, null);
                gpu.Surface = window.CreateVulkanSurface(gpu.Instance);
                VulkanSwapchain.Create(gpu, (uint)window.Width, (uint)window.Height);
                VulkanDevice.CreateRenderFinishedSemaphores(gpu);
                CreateTransientResources();
                Log.Info(Tag, $"Surface recreated — {gpu.SwapchainExtent.Width}x{gpu.SwapchainExtent.Height}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"Render thread crashed: {ex}");
        }
    }

    [DllImport("android")]
    private static extern nint ANativeWindow_fromSurface(nint env, nint surface);
}
