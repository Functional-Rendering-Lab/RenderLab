using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using RenderLab.Assets;
using RenderLab.Gpu;
using RenderLab.Gpu.Assets;
using RenderLab.Papers;
using RenderLab.Scene;
using RenderLab.Ui;
using Buffer = Silk.NET.Vulkan.Buffer;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.Pipelines;

using Scene = RenderLab.Scene.Scene;

// ─── Post 3: G-Buffer Only ──────────────────────────────────────────
// Matches blog post 3: "What a Frame Knows Before It Sees the Light."
// Renders one cube into structured G-Buffer textures and visualises any
// of them via a fullscreen pass — no lighting, no tonemap, no render
// graph. Manual barriers stand in for the graph compiler so the cost of
// hand-managed sync stays visible.

/// <summary>
/// Standalone G-Buffer pipeline: one mesh, four debug visualizations,
/// hand-managed barriers. It consumes no editor scene - it draws one cube of
/// its own - but it reads the editor's camera and visualization like every
/// other pipeline, so the panels that steer it are the ones already on screen.
/// </summary>
public sealed class GBufferPipeline : IPipeline
{
    public string Id => "gbuffer";
    public bool ConsumesScenes => false;

    /// <summary>
    /// The four attachments this pipeline fills, and no more: there is no lighting pass here, so
    /// there is no Final and no HDR to resolve. The Visualization panel offers exactly this list.
    /// </summary>
    public ImmutableArray<VisualizationMode> SupportedVisualizations { get; } =
    [
        VisualizationMode.Position, VisualizationMode.Normal,
        VisualizationMode.Albedo, VisualizationMode.Depth,
    ];

    GpuState gpu = null!;

    // Mesh
    uint indexCount;
    Buffer vertexBuffer, indexBuffer;
    Allocation vertexAlloc, indexAlloc;

    // Render passes
    RenderPass gbufferRenderPass;
    RenderPass viewportRenderPass;

    // Pipelines
    Pipeline gbufferPipeline;
    PipelineLayout gbufferPipelineLayout;
    Pipeline debugVizPipeline;
    PipelineLayout debugVizPipelineLayout;

    // Descriptor layouts
    DescriptorSetLayout singleDsLayout;
    DescriptorSetLayout materialDsLayout;
    DescriptorSetLayout cameraDsLayout;

    // Materials (white fallback only — registry owned by Application)
    MaterialDescriptors materials = null!;

    // This frame's camera and mode, taken from UiModel at the top of RecordFrame and read by the
    // recorders below. Not state: the editor holds both, and these are what it said this frame.
    Camera camera = null!;
    VisualizationMode vizMode;

    // Per-frame camera UBOs (host-visible, persistently mapped). One mat4 each.
    Buffer[] cameraBuffers = [];
    Allocation[] cameraAllocs = [];
    IntPtr[] cameraMapped = [];
    DescriptorPool cameraDescPool;
    DescriptorSet[] cameraDescSets = [];

    // Transient
    Sampler sampler;
    Image gbufferPosImage, gbufferNormImage, gbufferAlbImage, depthImage;
    Allocation gbufferPosAlloc, gbufferNormAlloc, gbufferAlbAlloc, depthAlloc;
    ImageView gbufferPosView, gbufferNormView, gbufferAlbView, depthView;
    Framebuffer gbufferFramebuffer;
    Framebuffer viewportFramebuffer;
    Extent2D viewportExtent;
    DescriptorPool debugVizDescPool;
    DescriptorSet[] debugVizPositionSets = [], debugVizNormalSets = [];
    DescriptorSet[] debugVizAlbedoSets = [], debugVizDepthSets = [];

    public void Initialize(GpuState gpuState, AssetRegistry assets)
    {
        gpu = gpuState;

        var mesh = ObjLoader.CreateCube();
        indexCount = (uint)mesh.Indices.Length;

        Console.WriteLine("RenderLab — Post 3: G-Buffer Visualization");
        Console.WriteLine($"  Mesh: {mesh.Vertices.Length} vertices, {mesh.Indices.Length / 3} triangles");

        (vertexBuffer, vertexAlloc) = VulkanBuffer.Create<Vertex3D>(gpu, BufferUsageFlags.VertexBufferBit, mesh.Vertices);
        (indexBuffer, indexAlloc)   = VulkanBuffer.Create<uint>(gpu, BufferUsageFlags.IndexBufferBit, mesh.Indices);

        gbufferRenderPass  = VulkanPipeline.CreateGBufferRenderPass(gpu);
        viewportRenderPass = VulkanPipeline.CreateViewportRenderPass(gpu);

        singleDsLayout   = VulkanDescriptors.CreateSamplerLayout(gpu);
        materialDsLayout = VulkanDescriptors.CreateSamplerLayout(gpu);
        cameraDsLayout   = VulkanDescriptors.CreateUniformBufferLayout(gpu, ShaderStageFlags.VertexBit);

        materials = new MaterialDescriptors(gpu, assets, materialDsLayout, maxTextures: 4);

        BuildPipelines();

        sampler = VulkanImage.CreateSampler(gpu);
        CreateCameraBuffers();

        Console.WriteLine("  No render graph — manual barriers between passes");
    }

    unsafe void BuildPipelines()
    {
        var shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
        byte[] LoadSpv(string name) => File.ReadAllBytes(Path.Combine(shaderDir, name));
        var gbufferVert = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.vert.spv"));
        var gbufferFrag = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.frag.spv"));
        var fsVert      = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("fullscreen.vert.spv"));
        var debugFrag   = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("debugviz.frag.spv"));

        gbufferPipeline = VulkanPipeline.CreateGBufferPipeline(
            gpu, gbufferRenderPass, gbufferVert, gbufferFrag,
            Vertex3D.BindingDescription, Vertex3D.AttributeDescriptions,
            (uint)Marshal.SizeOf<GBufferPushConstants>(),
            materialDsLayout,
            cameraDsLayout,
            out gbufferPipelineLayout);

        debugVizPipeline = VulkanPipeline.CreateFullscreenPipeline(
            gpu, viewportRenderPass, singleDsLayout, fsVert, debugFrag,
            (uint)Marshal.SizeOf<DebugVizPushConstants>(), ShaderStageFlags.FragmentBit,
            out debugVizPipelineLayout);

        gpu.Vk.DestroyShaderModule(gpu.Device, gbufferVert, null);
        gpu.Vk.DestroyShaderModule(gpu.Device, gbufferFrag, null);
        gpu.Vk.DestroyShaderModule(gpu.Device, fsVert, null);
        gpu.Vk.DestroyShaderModule(gpu.Device, debugFrag, null);
    }

    public unsafe void ReloadShaders(GpuState _)
    {
        gpu.Vk.DestroyPipeline(gpu.Device, gbufferPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, gbufferPipelineLayout, null);
        gpu.Vk.DestroyPipeline(gpu.Device, debugVizPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, debugVizPipelineLayout, null);
        BuildPipelines();
    }

    public void RecreateTransient(GpuState _, RenderTarget target)
    {
        DestroyTransient();
        viewportExtent = target.Extent;
        uint w = viewportExtent.Width, h = viewportExtent.Height;

        (gbufferPosImage,  gbufferPosAlloc,  gbufferPosView)  = VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferPositionFormat, w, h);
        (gbufferNormImage, gbufferNormAlloc, gbufferNormView) = VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferNormalFormat, w, h);
        (gbufferAlbImage,  gbufferAlbAlloc,  gbufferAlbView)  = VulkanImage.CreateOffscreen(gpu, VulkanPipeline.GBufferAlbedoFormat, w, h);
        (depthImage, depthAlloc, depthView) = VulkanImage.CreateDepthImage(gpu, w, h, gpu.Capabilities.DepthFormat, samplable: true);

        gbufferFramebuffer = VulkanPipeline.CreateGBufferFramebuffer(
            gpu, gbufferRenderPass, gbufferPosView, gbufferNormView, gbufferAlbView, depthView, w, h);
        viewportFramebuffer = VulkanPipeline.CreateOffscreenFramebuffer(
            gpu, viewportRenderPass, target.View, w, h);

        uint frames = (uint)GpuState.MaxFramesInFlight;
        debugVizDescPool      = VulkanDescriptors.CreatePool(gpu, frames * 4, 1);
        debugVizPositionSets  = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferPosView, sampler);
        debugVizNormalSets    = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferNormView, sampler);
        debugVizAlbedoSets    = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferAlbView, sampler);
        debugVizDepthSets     = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, depthView, sampler,
            ImageLayout.DepthStencilReadOnlyOptimal);
    }

    public void RecordFrame(GpuState _, CommandBuffer cb, Scene? __, UiModel ui, double ___, RenderTarget target)
    {
        // The editor drives this pipeline now. Which attachment reaches the screen is the
        // Visualization panel's, and the camera is the Inspector's - both used to be private
        // fields behind two ImGui windows this file drew for itself, which is how a rendering
        // technique ended up with a UI framework among its dependencies and two controls the
        // editor could not see.
        vizMode = ui.Viz;

        // The viewport's shape, not the window's. A cube rendered at the window's aspect and then
        // shown inside a panel of a different one is a cube that is subtly the wrong shape, and
        // nothing on screen says why.
        camera = FreeCameraController.ToCamera(ui.Camera,
            (float)target.Extent.Width / target.Extent.Height);

        RecordGBufferPass(cb);
        InsertGBufferBarriers(cb);
        RecordDebugVizPass(cb);
    }

    void RecordGBufferPass(CommandBuffer cb)
    {
        var resources = new GBufferPassResources(
            RenderPass: gbufferRenderPass,
            Framebuffer: gbufferFramebuffer,
            Pipeline: gbufferPipeline,
            PipelineLayout: gbufferPipelineLayout,
            Extent: viewportExtent);

        UploadCameraForCurrentFrame(camera);
        var pc = GBufferPass.BuildPushConstants(Transform.Default, MaterialParams.Default);
        var matSet = materials.GetOrAllocate(TextureId.None);
        GBufferPass.Record(gpu.Vk, cb, resources, pc, matSet,
            cameraDescSets[gpu.CurrentFrame], vertexBuffer, indexBuffer, indexCount);
    }

    unsafe void UploadCameraForCurrentFrame(Camera cam)
    {
        var vp = cam.ViewProjectionMatrix;
        var dst = (Matrix4x4*)cameraMapped[gpu.CurrentFrame];
        *dst = vp;
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

    unsafe void InsertGBufferBarriers(CommandBuffer cb)
    {
        var barriers = stackalloc ImageMemoryBarrier[3];
        barriers[0] = MakeColorBarrier(gbufferPosImage);
        barriers[1] = MakeColorBarrier(gbufferNormImage);
        barriers[2] = MakeColorBarrier(gbufferAlbImage);
        gpu.Vk.CmdPipelineBarrier(cb,
            PipelineStageFlags.ColorAttachmentOutputBit,
            PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 3, barriers);

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
                    BaseMipLevel = 0, LevelCount = 1, BaseArrayLayer = 0, LayerCount = 1,
                },
            };
            gpu.Vk.CmdPipelineBarrier(cb,
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
                BaseMipLevel = 0, LevelCount = 1, BaseArrayLayer = 0, LayerCount = 1,
            },
        };
    }

    unsafe void RecordDebugVizPass(CommandBuffer cb)
    {
        var sourceSet = vizMode switch
        {
            VisualizationMode.Position => debugVizPositionSets[gpu.CurrentFrame],
            VisualizationMode.Normal   => debugVizNormalSets[gpu.CurrentFrame],
            VisualizationMode.Albedo   => debugVizAlbedoSets[gpu.CurrentFrame],
            VisualizationMode.Depth    => debugVizDepthSets[gpu.CurrentFrame],
            _ => debugVizPositionSets[gpu.CurrentFrame],
        };
        var resources = new DebugVizPassResources(
            RenderPass: viewportRenderPass,
            Framebuffer: viewportFramebuffer,
            Pipeline: debugVizPipeline,
            PipelineLayout: debugVizPipelineLayout,
            SourceSet: sourceSet,
            Extent: viewportExtent);
        var pc = DebugVizPass.BuildPushConstants(vizMode == VisualizationMode.Depth, camera);
        DebugVizPass.Record(gpu.Vk, cb, resources, pc);

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
                    BaseMipLevel = 0, LevelCount = 1, BaseArrayLayer = 0, LayerCount = 1,
                },
            };
            gpu.Vk.CmdPipelineBarrier(cb,
                PipelineStageFlags.FragmentShaderBit,
                PipelineStageFlags.EarlyFragmentTestsBit,
                0, 0, null, 0, null, 1, &depthBarrier);
        }
    }

    unsafe void DestroyTransient()
    {
        if (viewportFramebuffer.Handle != 0)
            gpu.Vk.DestroyFramebuffer(gpu.Device, viewportFramebuffer, null);
        if (debugVizDescPool.Handle != 0)
            gpu.Vk.DestroyDescriptorPool(gpu.Device, debugVizDescPool, null);
        if (gbufferFramebuffer.Handle != 0)
            gpu.Vk.DestroyFramebuffer(gpu.Device, gbufferFramebuffer, null);
        if (depthView.Handle != 0)
            VulkanImage.DestroyOffscreen(gpu, depthImage, depthAlloc, depthView);
        if (gbufferAlbView.Handle != 0)
            VulkanImage.DestroyOffscreen(gpu, gbufferAlbImage, gbufferAlbAlloc, gbufferAlbView);
        if (gbufferNormView.Handle != 0)
            VulkanImage.DestroyOffscreen(gpu, gbufferNormImage, gbufferNormAlloc, gbufferNormView);
        if (gbufferPosView.Handle != 0)
            VulkanImage.DestroyOffscreen(gpu, gbufferPosImage, gbufferPosAlloc, gbufferPosView);
        viewportFramebuffer = default;
        debugVizDescPool = default;
        gbufferFramebuffer = default;
        depthView = default;
        gbufferAlbView = default;
        gbufferNormView = default;
        gbufferPosView = default;
    }

    public unsafe void Dispose()
    {
        gpu.Vk.DeviceWaitIdle(gpu.Device);
        DestroyTransient();
        DestroyCameraBuffers();
        gpu.Vk.DestroySampler(gpu.Device, sampler, null);
        gpu.Vk.DestroyPipeline(gpu.Device, gbufferPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, gbufferPipelineLayout, null);
        gpu.Vk.DestroyRenderPass(gpu.Device, gbufferRenderPass, null);
        gpu.Vk.DestroyPipeline(gpu.Device, debugVizPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, debugVizPipelineLayout, null);
        gpu.Vk.DestroyRenderPass(gpu.Device, viewportRenderPass, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, singleDsLayout, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, materialDsLayout, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, cameraDsLayout, null);
        materials.Dispose();
        VulkanBuffer.Destroy(gpu, vertexBuffer, vertexAlloc);
        VulkanBuffer.Destroy(gpu, indexBuffer, indexAlloc);
    }
}
