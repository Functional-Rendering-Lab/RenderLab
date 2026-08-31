using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using RenderLab.Assets;
using RenderLab.Gpu;
using RenderLab.Gpu.Assets;
using RenderLab.Gpu.Debug;
using RenderLab.Graph;
using RenderLab.Papers;
using RenderLab.Scene;
using RenderLab.Ui;
using Buffer = Silk.NET.Vulkan.Buffer;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.Pipelines;

using Scene = RenderLab.Scene.Scene;

// ─── M3+: Deferred Baseline ─────────────────────────────────────────
// GBuffer → Lighting → Tonemap. Reads scene drawables/lights and reads
// editor knobs (shading, ambient, lighting-only, viz, clear) from
// UiModel. The render graph compiler resolves barriers; manual
// transitions only handle the depth-attachment ↔ depth-sampled swap
// for the depth visualisation viz.

/// <summary>
/// The deferred pipeline. <c>ConsumesScenes</c>; reads UiModel for shading
/// / ambient / lighting-only / viz / clear-colour. The Application appends
/// the ImGui overlay pass after <see cref="RecordFrame"/>.
/// </summary>
public sealed class DeferredPipeline : IPipeline
{
    public string Id => "deferred";
    public bool ConsumesScenes => true;

    const int MaxLights = 64;

    GpuState gpu = null!;
    AssetRegistry assets = null!;
    public DescriptorSetLayout MaterialDsLayout { get; private set; }
    MaterialDescriptors materials = null!;

    /// <summary>The Application uses this to wire the asset browser's edit
    /// pipeline; pipeline-internal state stays read-only from outside.</summary>
    public MaterialDescriptors Materials => materials;

    // Render passes
    RenderPass gbufferRenderPass, lightingRenderPass, tonemapRenderPass;

    // Descriptor layouts
    DescriptorSetLayout gbufferDsLayout, singleDsLayout, lightStorageDsLayout, cameraDsLayout;

    // Pipelines
    Pipeline gbufferPipeline, lightingPipeline, tonemapPipeline, debugVizPipeline;
    PipelineLayout gbufferPipelineLayout, lightingPipelineLayout, tonemapPipelineLayout, debugVizPipelineLayout;

    // Transient (recreated on resize)
    Sampler sampler;
    Image gbufferPosImage, gbufferNormImage, gbufferAlbImage, depthImage, hdrImage;
    Allocation gbufferPosAlloc, gbufferNormAlloc, gbufferAlbAlloc, depthAlloc, hdrAlloc;
    ImageView gbufferPosView, gbufferNormView, gbufferAlbView, depthView, hdrView;
    Framebuffer gbufferFramebuffer, lightingFramebuffer;
    Framebuffer[] swapchainFramebuffers = [];
    DescriptorPool gbufferDescPool, tonemapDescPool, debugVizDescPool, lightDescPool;
    DescriptorSet[] gbufferDescSets = [];
    DescriptorSet[] tonemapDescSets = [];
    DescriptorSet[] debugVizPositionSets = [], debugVizNormalSets = [], debugVizAlbedoSets = [];
    DescriptorSet[] debugVizDepthSets = [], debugVizHdrSets = [];

    // Per-frame light SSBOs (host-visible, persistently mapped)
    Buffer[] lightBuffers = [];
    Allocation[] lightAllocs = [];
    IntPtr[] lightMapped = [];
    DescriptorSet[] lightDescSets = [];

    // Per-frame camera UBOs (host-visible, persistently mapped). One mat4 each.
    Buffer[] cameraBuffers = [];
    Allocation[] cameraAllocs = [];
    IntPtr[] cameraMapped = [];
    DescriptorPool cameraDescPool;
    DescriptorSet[] cameraDescSets = [];

    // Stats + render graph
    GpuTimestamps timestamps = null!;
    ImmutableArray<ResolvedPass> resolvedPasses;
    ResourceName gPosition, gNormal, gAlbedo, hdrColor, backbuffer;

    // Captured each RecordFrame so the GBuffer recorder closure can read
    // the per-frame scene without an extra parameter.
    Scene? currentScene;
    UiModel currentUi = UiModel.Default;

    public void Initialize(GpuState gpuState, AssetRegistry assetRegistry, RenderPass overlayRenderPass)
    {
        gpu = gpuState;
        assets = assetRegistry;

        Console.WriteLine("RenderLab — Deferred Pipeline");

        gbufferRenderPass  = VulkanPipeline.CreateGBufferRenderPass(gpu);
        lightingRenderPass = VulkanPipeline.CreateOffscreenRenderPass(gpu, VulkanPipeline.HdrFormat);
        tonemapRenderPass  = VulkanPipeline.CreateRenderPass(gpu);

        gbufferDsLayout      = VulkanDescriptors.CreateGBufferSamplerLayout(gpu);
        singleDsLayout       = VulkanDescriptors.CreateSamplerLayout(gpu);
        lightStorageDsLayout = VulkanDescriptors.CreateLightStorageLayout(gpu);
        cameraDsLayout       = VulkanDescriptors.CreateUniformBufferLayout(gpu, ShaderStageFlags.VertexBit);
        MaterialDsLayout     = VulkanDescriptors.CreateSamplerLayout(gpu);

        materials = new MaterialDescriptors(gpu, assets, MaterialDsLayout, maxTextures: 16);

        BuildPipelines();

        sampler = VulkanImage.CreateSampler(gpu);
        CreateLightBuffers();
        CreateCameraBuffers();

        timestamps = GpuTimestamps.Create(gpu, 3);

        gPosition  = ResourceName.Of("GBuffer.Position");
        gNormal    = ResourceName.Of("GBuffer.Normal");
        gAlbedo    = ResourceName.Of("GBuffer.Albedo");
        hdrColor   = ResourceName.Of("HDR");
        backbuffer = ResourceName.Of("Backbuffer");

        var passes = ImmutableArray.Create(
            new RenderPassDeclaration("GBuffer",
                Inputs: [],
                Outputs: [
                    new PassOutput(gPosition, ResourceUsage.ColorAttachmentWrite),
                    new PassOutput(gNormal,   ResourceUsage.ColorAttachmentWrite),
                    new PassOutput(gAlbedo,   ResourceUsage.ColorAttachmentWrite),
                ]),
            new RenderPassDeclaration("Lighting",
                Inputs: [
                    new PassInput(gPosition, ResourceUsage.ShaderRead),
                    new PassInput(gNormal,   ResourceUsage.ShaderRead),
                    new PassInput(gAlbedo,   ResourceUsage.ShaderRead),
                ],
                Outputs: [new PassOutput(hdrColor, ResourceUsage.ColorAttachmentWrite)]),
            new RenderPassDeclaration("Tonemap",
                Inputs: [new PassInput(hdrColor, ResourceUsage.ShaderRead)],
                Outputs: [new PassOutput(backbuffer, ResourceUsage.Present)]));

        resolvedPasses = RenderGraphCompiler.Compile(passes).Match(
            ok: r => r,
            error: e => throw new InvalidOperationException($"Render graph compile failed: {e}"));

        Console.WriteLine($"  Swapchain: {gpu.SwapchainExtent.Width}x{gpu.SwapchainExtent.Height}");
        Console.WriteLine($"  Passes: {string.Join(" -> ", resolvedPasses.Select(p => p.Declaration.Name))}");
        Console.WriteLine($"  Barriers: {resolvedPasses.Sum(p => p.BarriersBefore.Length)}");
    }

    unsafe void BuildPipelines()
    {
        var shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
        byte[] LoadSpv(string name) => File.ReadAllBytes(Path.Combine(shaderDir, name));

        var gbufferVert    = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.vert.spv"));
        var gbufferFrag    = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.frag.spv"));
        var fsVert         = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("fullscreen.vert.spv"));
        var lightingFrag   = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("lighting.frag.spv"));
        var tonemapFrag    = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("tonemap.frag.spv"));
        var debugVizFrag   = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("debugviz.frag.spv"));

        gbufferPipeline = VulkanPipeline.CreateGBufferPipeline(
            gpu, gbufferRenderPass, gbufferVert, gbufferFrag,
            Vertex3D.BindingDescription, Vertex3D.AttributeDescriptions,
            (uint)Marshal.SizeOf<GBufferPushConstants>(),
            MaterialDsLayout,
            cameraDsLayout,
            out gbufferPipelineLayout);

        lightingPipeline = VulkanPipeline.CreateFullscreenPipeline(
            gpu, lightingRenderPass,
            new[] { gbufferDsLayout, lightStorageDsLayout },
            fsVert, lightingFrag,
            (uint)Marshal.SizeOf<LightingPushConstants>(), ShaderStageFlags.FragmentBit,
            out lightingPipelineLayout);

        tonemapPipeline = VulkanPipeline.CreateFullscreenPipeline(
            gpu, tonemapRenderPass, singleDsLayout, fsVert, tonemapFrag,
            0, ShaderStageFlags.None,
            out tonemapPipelineLayout);

        debugVizPipeline = VulkanPipeline.CreateFullscreenPipeline(
            gpu, tonemapRenderPass, singleDsLayout, fsVert, debugVizFrag,
            (uint)Marshal.SizeOf<DebugVizPushConstants>(), ShaderStageFlags.FragmentBit,
            out debugVizPipelineLayout);

        gpu.Vk.DestroyShaderModule(gpu.Device, gbufferVert, null);
        gpu.Vk.DestroyShaderModule(gpu.Device, gbufferFrag, null);
        gpu.Vk.DestroyShaderModule(gpu.Device, fsVert, null);
        gpu.Vk.DestroyShaderModule(gpu.Device, lightingFrag, null);
        gpu.Vk.DestroyShaderModule(gpu.Device, tonemapFrag, null);
        gpu.Vk.DestroyShaderModule(gpu.Device, debugVizFrag, null);
    }

    public unsafe void ReloadShaders(GpuState _)
    {
        gpu.Vk.DestroyPipeline(gpu.Device, gbufferPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, gbufferPipelineLayout, null);
        gpu.Vk.DestroyPipeline(gpu.Device, lightingPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, lightingPipelineLayout, null);
        gpu.Vk.DestroyPipeline(gpu.Device, tonemapPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, tonemapPipelineLayout, null);
        gpu.Vk.DestroyPipeline(gpu.Device, debugVizPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, debugVizPipelineLayout, null);
        BuildPipelines();
    }

    public void RecreateTransient(GpuState _)
    {
        DestroyTransient();

        var extent = gpu.SwapchainExtent;
        uint w = extent.Width, h = extent.Height;

        (gbufferPosImage,  gbufferPosAlloc,  gbufferPosView)  = VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferPositionFormat, w, h);
        (gbufferNormImage, gbufferNormAlloc, gbufferNormView) = VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferNormalFormat, w, h);
        (gbufferAlbImage,  gbufferAlbAlloc,  gbufferAlbView)  = VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferAlbedoFormat, w, h);
        (depthImage, depthAlloc, depthView) = VulkanImage.CreateDepthImage(gpu, w, h, gpu.Capabilities.DepthFormat, samplable: true);
        (hdrImage,   hdrAlloc,   hdrView)   = VulkanImage.CreateOffscreen(gpu, VulkanPipeline.HdrFormat, w, h);

        gbufferFramebuffer = VulkanPipeline.CreateGBufferFramebuffer(
            gpu, gbufferRenderPass, gbufferPosView, gbufferNormView, gbufferAlbView, depthView, w, h);
        lightingFramebuffer = VulkanPipeline.CreateOffscreenFramebuffer(gpu, lightingRenderPass, hdrView, w, h);
        swapchainFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, tonemapRenderPass);

        uint frames = (uint)GpuState.MaxFramesInFlight;

        gbufferDescPool = VulkanDescriptors.CreatePool(gpu, frames, 3);
        gbufferDescSets = VulkanDescriptors.AllocateGBufferSets(
            gpu, gbufferDescPool, gbufferDsLayout, frames,
            gbufferPosView, gbufferNormView, gbufferAlbView, sampler);

        tonemapDescPool = VulkanDescriptors.CreatePool(gpu, frames, 1);
        tonemapDescSets = VulkanDescriptors.AllocateSets(
            gpu, tonemapDescPool, singleDsLayout, frames, hdrView, sampler);

        debugVizDescPool = VulkanDescriptors.CreatePool(gpu, frames * 5, 1);
        debugVizPositionSets = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferPosView, sampler);
        debugVizNormalSets   = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferNormView, sampler);
        debugVizAlbedoSets   = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferAlbView, sampler);
        debugVizDepthSets    = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, depthView, sampler,
            ImageLayout.DepthStencilReadOnlyOptimal);
        debugVizHdrSets      = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, hdrView, sampler);
    }

    public void TickStats() => timestamps.ReadResults();

    public void RecordFrame(GpuState _, CommandBuffer cb, Scene? scene, UiModel ui, double deltaSeconds, uint imageIndex)
    {
        currentScene = scene ?? throw new InvalidOperationException("DeferredPipeline requires a scene");
        currentUi    = ui;

        timestamps.Reset(gpu.Vk, cb);

        var resourceImages = new Dictionary<ResourceName, Image>
        {
            [gPosition]  = gbufferPosImage,
            [gNormal]    = gbufferNormImage,
            [gAlbedo]    = gbufferAlbImage,
            [hdrColor]   = hdrImage,
            [backbuffer] = gpu.SwapchainImages[imageIndex],
        };

        var passRecorders = new Dictionary<string, Action<Vk, CommandBuffer>>
        {
            ["GBuffer"]  = (api, c) => RecordGBufferPass(api, c),
            ["Lighting"] = (api, c) => RecordLightingPass(api, c),
            ["Tonemap"]  = (api, c) => RecordTonemapPass(api, c, imageIndex),
        };

        VulkanGraphExecutor.Execute(gpu, cb, resolvedPasses, passRecorders, resourceImages);
    }

    public FrameStats GetFrameStats(double deltaSeconds) => new(
        DeltaSeconds: (float)deltaSeconds,
        TimestampLabels: timestamps.Labels.ToArray(),
        TimestampMillis: timestamps.TimingsMs.ToArray(),
        ResolvedPasses: resolvedPasses);

    void RecordGBufferPass(Vk api, CommandBuffer cb)
    {
        timestamps.BeginPass(api, cb, "GBuffer");
        var resources = new GBufferPassResources(
            RenderPass: gbufferRenderPass,
            Framebuffer: gbufferFramebuffer,
            Pipeline: gbufferPipeline,
            PipelineLayout: gbufferPipelineLayout,
            Extent: gpu.SwapchainExtent);
        var scene = currentScene!;
        UploadCameraForCurrentFrame(scene.Camera);
        GBufferPass.Record(api, cb, resources, scene.Drawables, assets, assets, materials,
            cameraDescSets[gpu.CurrentFrame]);
        timestamps.EndPass(api, cb);
    }

    unsafe void UploadCameraForCurrentFrame(Camera camera)
    {
        var vp = camera.ViewProjectionMatrix;
        var dst = (Matrix4x4*)cameraMapped[gpu.CurrentFrame];
        *dst = vp;
    }

    void RecordLightingPass(Vk api, CommandBuffer cb)
    {
        timestamps.BeginPass(api, cb, "Lighting");
        var lightCount = UploadLightsForCurrentFrame();
        var ui = currentUi;
        var scene = currentScene!;
        var resources = new LightingPassResources(
            RenderPass: lightingRenderPass,
            Framebuffer: lightingFramebuffer,
            Pipeline: lightingPipeline,
            PipelineLayout: lightingPipelineLayout,
            GBufferDescriptorSet: gbufferDescSets[gpu.CurrentFrame],
            LightDescriptorSet: lightDescSets[gpu.CurrentFrame],
            Extent: gpu.SwapchainExtent);
        var pc = DeferredLighting.BuildPushConstants(
            scene.Camera, lightCount, ui.Shading, ui.Ambient, ui.LightingOnly, (int)ui.Background);
        DeferredLighting.Record(api, cb, resources, pc, ui.ClearColor);
        timestamps.EndPass(api, cb);
    }

    unsafe int UploadLightsForCurrentFrame()
    {
        var scene = currentScene!;
        var available = Math.Min(scene.Lights.Length, MaxLights);
        if (available == 0) return 0;
        var dst = new Span<GpuLight>((void*)lightMapped[gpu.CurrentFrame], MaxLights);
        return LightPacking.PackInto(scene.Lights.AsSpan(0, available), dst);
    }

    void RecordTonemapPass(Vk api, CommandBuffer cb, uint imageIndex)
    {
        timestamps.BeginPass(api, cb, "Tonemap");
        var ui = currentUi;

        if (ui.Viz == VisualizationMode.Depth)
            TransitionDepthForSampling(api, cb);

        if (ui.Viz == VisualizationMode.Final)
        {
            var resources = new TonemapPassResources(
                RenderPass: tonemapRenderPass,
                Framebuffer: swapchainFramebuffers[imageIndex],
                Pipeline: tonemapPipeline,
                PipelineLayout: tonemapPipelineLayout,
                HdrSet: tonemapDescSets[gpu.CurrentFrame],
                Extent: gpu.SwapchainExtent);
            TonemapPass.Record(api, cb, resources);
        }
        else
        {
            var sourceSet = ui.Viz switch
            {
                VisualizationMode.Position => debugVizPositionSets[gpu.CurrentFrame],
                VisualizationMode.Normal   => debugVizNormalSets[gpu.CurrentFrame],
                VisualizationMode.Albedo   => debugVizAlbedoSets[gpu.CurrentFrame],
                VisualizationMode.Depth    => debugVizDepthSets[gpu.CurrentFrame],
                VisualizationMode.HDR      => debugVizHdrSets[gpu.CurrentFrame],
                _ => tonemapDescSets[gpu.CurrentFrame],
            };
            var resources = new DebugVizPassResources(
                RenderPass: tonemapRenderPass,
                Framebuffer: swapchainFramebuffers[imageIndex],
                Pipeline: debugVizPipeline,
                PipelineLayout: debugVizPipelineLayout,
                SourceSet: sourceSet,
                Extent: gpu.SwapchainExtent);
            var pc = DebugVizPass.BuildPushConstants(ui.Viz == VisualizationMode.Depth, currentScene!.Camera);
            DebugVizPass.Record(api, cb, resources, pc);
        }

        if (ui.Viz == VisualizationMode.Depth)
            TransitionDepthForAttachment(api, cb);

        timestamps.EndPass(api, cb);
    }

    unsafe void TransitionDepthForSampling(Vk api, CommandBuffer cb)
    {
        var b = new ImageMemoryBarrier
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
                BaseMipLevel = 0, LevelCount = 1, BaseArrayLayer = 0, LayerCount = 1,
            },
        };
        api.CmdPipelineBarrier(cb,
            PipelineStageFlags.LateFragmentTestsBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, &b);
    }

    unsafe void TransitionDepthForAttachment(Vk api, CommandBuffer cb)
    {
        var b = new ImageMemoryBarrier
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
                BaseMipLevel = 0, LevelCount = 1, BaseArrayLayer = 0, LayerCount = 1,
            },
        };
        api.CmdPipelineBarrier(cb,
            PipelineStageFlags.FragmentShaderBit,
            PipelineStageFlags.EarlyFragmentTestsBit,
            0, 0, null, 0, null, 1, &b);
    }

    unsafe void CreateLightBuffers()
    {
        int frames = GpuState.MaxFramesInFlight;
        ulong size = (ulong)(MaxLights * sizeof(GpuLight));
        lightBuffers = new Buffer[frames];
        lightAllocs  = new Allocation[frames];
        lightMapped  = new IntPtr[frames];
        for (int i = 0; i < frames; i++)
        {
            var (buf, alloc) = gpu.Allocator.AllocateBuffer(
                gpu, size, BufferUsageFlags.StorageBufferBit, MemoryIntent.CpuToGpu);
            lightBuffers[i] = buf;
            lightAllocs[i] = alloc;
            lightMapped[i] = (IntPtr)gpu.Allocator.Map(gpu, alloc);
        }
        lightDescPool = VulkanDescriptors.CreateStorageBufferPool(gpu, (uint)frames);
        lightDescSets = VulkanDescriptors.AllocateStorageBufferSets(
            gpu, lightDescPool, lightStorageDsLayout, lightBuffers, size);
    }

    unsafe void DestroyLightBuffers()
    {
        if (lightBuffers.Length == 0) return;
        gpu.Vk.DestroyDescriptorPool(gpu.Device, lightDescPool, null);
        for (int i = 0; i < lightBuffers.Length; i++)
        {
            gpu.Allocator.Unmap(gpu, lightAllocs[i]);
            VulkanBuffer.Destroy(gpu, lightBuffers[i], lightAllocs[i]);
        }
        lightBuffers = [];
        lightAllocs  = [];
        lightMapped  = [];
        lightDescSets = [];
    }

    unsafe void CreateCameraBuffers()
    {
        int frames = GpuState.MaxFramesInFlight;
        ulong size = (ulong)sizeof(Matrix4x4);
        cameraBuffers = new Buffer[frames];
        cameraAllocs  = new Allocation[frames];
        cameraMapped  = new IntPtr[frames];
        for (int i = 0; i < frames; i++)
        {
            var (buf, alloc) = gpu.Allocator.AllocateBuffer(
                gpu, size, BufferUsageFlags.UniformBufferBit, MemoryIntent.CpuToGpu);
            cameraBuffers[i] = buf;
            cameraAllocs[i]  = alloc;
            cameraMapped[i]  = (IntPtr)gpu.Allocator.Map(gpu, alloc);
        }
        cameraDescPool = VulkanDescriptors.CreateUniformBufferPool(gpu, (uint)frames);
        cameraDescSets = VulkanDescriptors.AllocateUniformBufferSets(
            gpu, cameraDescPool, cameraDsLayout, cameraBuffers, size);
    }

    unsafe void DestroyCameraBuffers()
    {
        if (cameraBuffers.Length == 0) return;
        gpu.Vk.DestroyDescriptorPool(gpu.Device, cameraDescPool, null);
        for (int i = 0; i < cameraBuffers.Length; i++)
        {
            gpu.Allocator.Unmap(gpu, cameraAllocs[i]);
            VulkanBuffer.Destroy(gpu, cameraBuffers[i], cameraAllocs[i]);
        }
        cameraBuffers = [];
        cameraAllocs  = [];
        cameraMapped  = [];
        cameraDescSets = [];
    }

    unsafe void DestroyTransient()
    {
        if (swapchainFramebuffers.Length > 0)
            VulkanPipeline.DestroyFramebuffers(gpu, swapchainFramebuffers);
        if (debugVizDescPool.Handle != 0)
            gpu.Vk.DestroyDescriptorPool(gpu.Device, debugVizDescPool, null);
        if (tonemapDescPool.Handle != 0)
            gpu.Vk.DestroyDescriptorPool(gpu.Device, tonemapDescPool, null);
        if (gbufferDescPool.Handle != 0)
            gpu.Vk.DestroyDescriptorPool(gpu.Device, gbufferDescPool, null);
        if (lightingFramebuffer.Handle != 0)
            gpu.Vk.DestroyFramebuffer(gpu.Device, lightingFramebuffer, null);
        if (gbufferFramebuffer.Handle != 0)
            gpu.Vk.DestroyFramebuffer(gpu.Device, gbufferFramebuffer, null);
        if (hdrView.Handle != 0)
            VulkanImage.DestroyOffscreen(gpu, hdrImage, hdrAlloc, hdrView);
        if (depthView.Handle != 0)
            VulkanImage.DestroyOffscreen(gpu, depthImage, depthAlloc, depthView);
        if (gbufferAlbView.Handle != 0)
            VulkanImage.DestroyOffscreen(gpu, gbufferAlbImage, gbufferAlbAlloc, gbufferAlbView);
        if (gbufferNormView.Handle != 0)
            VulkanImage.DestroyOffscreen(gpu, gbufferNormImage, gbufferNormAlloc, gbufferNormView);
        if (gbufferPosView.Handle != 0)
            VulkanImage.DestroyOffscreen(gpu, gbufferPosImage, gbufferPosAlloc, gbufferPosView);
        swapchainFramebuffers = [];
        debugVizDescPool = default;
        tonemapDescPool = default;
        gbufferDescPool = default;
        lightingFramebuffer = default;
        gbufferFramebuffer = default;
        hdrView = default;
        depthView = default;
        gbufferAlbView = default;
        gbufferNormView = default;
        gbufferPosView = default;
    }

    public unsafe void Dispose()
    {
        gpu.Vk.DeviceWaitIdle(gpu.Device);
        timestamps.Dispose();
        DestroyTransient();
        DestroyLightBuffers();
        DestroyCameraBuffers();

        gpu.Vk.DestroySampler(gpu.Device, sampler, null);
        gpu.Vk.DestroyPipeline(gpu.Device, gbufferPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, gbufferPipelineLayout, null);
        gpu.Vk.DestroyRenderPass(gpu.Device, gbufferRenderPass, null);
        gpu.Vk.DestroyPipeline(gpu.Device, lightingPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, lightingPipelineLayout, null);
        gpu.Vk.DestroyRenderPass(gpu.Device, lightingRenderPass, null);
        gpu.Vk.DestroyPipeline(gpu.Device, tonemapPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, tonemapPipelineLayout, null);
        gpu.Vk.DestroyPipeline(gpu.Device, debugVizPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, debugVizPipelineLayout, null);
        gpu.Vk.DestroyRenderPass(gpu.Device, tonemapRenderPass, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, gbufferDsLayout, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, singleDsLayout, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, lightStorageDsLayout, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, cameraDsLayout, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, MaterialDsLayout, null);
        materials.Dispose();
    }

    /// <summary>Forwarded so the Application's editor can invalidate the
    /// material descriptor cache when a texture is removed.</summary>
    public void InvalidateMaterialTexture(TextureId id) => materials.InvalidateTexture(id);

    /// <summary>Drop the per-texture descriptor cache wholesale ahead of a
    /// scene swap; new sets get bound on the first <c>RecordFrame</c> after
    /// the new scene's textures register.</summary>
    void IPipeline.ResetSceneState() => materials.InvalidateAll();
}
