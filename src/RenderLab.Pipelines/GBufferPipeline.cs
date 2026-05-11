using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
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
using ImGui = ImGuiNET.ImGui;

// ─── Post 3: G-Buffer Only ──────────────────────────────────────────
// Matches blog post 3: "What a Frame Knows Before It Sees the Light."
// Renders one cube into structured G-Buffer textures and visualises any
// of them via a fullscreen pass — no lighting, no tonemap, no render
// graph. Manual barriers stand in for the graph compiler so the cost of
// hand-managed sync stays visible.

/// <summary>
/// Standalone G-Buffer pipeline: one mesh, four debug visualizations,
/// hand-managed barriers. Owns its camera state so it does not consume
/// editor scenes.
/// </summary>
public sealed class GBufferPipeline : IPipeline
{
    public string Id => "gbuffer";
    public bool ConsumesScenes => false;

    static readonly string[] ModeNames = ["Position", "Normal", "Albedo", "Depth"];
    static readonly VisualizationMode[] Modes =
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
    RenderPass swapchainRenderPass;

    // Pipelines
    Pipeline gbufferPipeline;
    PipelineLayout gbufferPipelineLayout;
    Pipeline debugVizPipeline;
    PipelineLayout debugVizPipelineLayout;

    // Descriptor layouts
    DescriptorSetLayout singleDsLayout;
    DescriptorSetLayout materialDsLayout;

    // Materials (white fallback only — registry owned by Application)
    MaterialDescriptors materials = null!;

    // Camera
    FreeCameraState cameraState;
    Camera camera = null!;
    VisualizationMode vizMode = VisualizationMode.Position;

    // Transient
    Sampler sampler;
    Image gbufferPosImage, gbufferNormImage, gbufferAlbImage, depthImage;
    Allocation gbufferPosAlloc, gbufferNormAlloc, gbufferAlbAlloc, depthAlloc;
    ImageView gbufferPosView, gbufferNormView, gbufferAlbView, depthView;
    Framebuffer gbufferFramebuffer;
    Framebuffer[] swapchainFramebuffers = [];
    DescriptorPool debugVizDescPool;
    DescriptorSet[] debugVizPositionSets = [], debugVizNormalSets = [];
    DescriptorSet[] debugVizAlbedoSets = [], debugVizDepthSets = [];

    public void Initialize(GpuState gpuState, AssetRegistry assets, RenderPass overlayRenderPass)
    {
        gpu = gpuState;

        var mesh = ObjLoader.CreateCube();
        indexCount = (uint)mesh.Indices.Length;

        Console.WriteLine("RenderLab — Post 3: G-Buffer Visualization");
        Console.WriteLine($"  Mesh: {mesh.Vertices.Length} vertices, {mesh.Indices.Length / 3} triangles");

        (vertexBuffer, vertexAlloc) = VulkanBuffer.Create<Vertex3D>(gpu, BufferUsageFlags.VertexBufferBit, mesh.Vertices);
        (indexBuffer, indexAlloc)   = VulkanBuffer.Create<uint>(gpu, BufferUsageFlags.IndexBufferBit, mesh.Indices);

        var shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
        byte[] LoadSpv(string name) => File.ReadAllBytes(Path.Combine(shaderDir, name));
        var gbufferVert = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.vert.spv"));
        var gbufferFrag = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("gbuffer.frag.spv"));
        var fsVert      = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("fullscreen.vert.spv"));
        var debugFrag   = VulkanPipeline.CreateShaderModule(gpu, LoadSpv("debugviz.frag.spv"));

        gbufferRenderPass   = VulkanPipeline.CreateGBufferRenderPass(gpu);
        swapchainRenderPass = VulkanPipeline.CreateRenderPass(gpu);

        singleDsLayout   = VulkanDescriptors.CreateSamplerLayout(gpu);
        materialDsLayout = VulkanDescriptors.CreateSamplerLayout(gpu);

        materials = new MaterialDescriptors(gpu, assets, materialDsLayout, maxTextures: 4);

        gbufferPipeline = VulkanPipeline.CreateGBufferPipeline(
            gpu, gbufferRenderPass, gbufferVert, gbufferFrag,
            Vertex3D.BindingDescription, Vertex3D.AttributeDescriptions,
            (uint)Marshal.SizeOf<GBufferPushConstants>(),
            materialDsLayout,
            out gbufferPipelineLayout);

        debugVizPipeline = VulkanPipeline.CreateFullscreenPipeline(
            gpu, swapchainRenderPass, singleDsLayout, fsVert, debugFrag,
            (uint)Marshal.SizeOf<DebugVizPushConstants>(), ShaderStageFlags.FragmentBit,
            out debugVizPipelineLayout);

        unsafe
        {
            gpu.Vk.DestroyShaderModule(gpu.Device, gbufferVert, null);
            gpu.Vk.DestroyShaderModule(gpu.Device, gbufferFrag, null);
            gpu.Vk.DestroyShaderModule(gpu.Device, fsVert, null);
            gpu.Vk.DestroyShaderModule(gpu.Device, debugFrag, null);
        }

        cameraState = FreeCameraController.CreateDefault();
        sampler = VulkanImage.CreateSampler(gpu);

        Console.WriteLine($"  Swapchain: {gpu.SwapchainExtent.Width}x{gpu.SwapchainExtent.Height}");
        Console.WriteLine("  No render graph — manual barriers between passes");
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

        gbufferFramebuffer = VulkanPipeline.CreateGBufferFramebuffer(
            gpu, gbufferRenderPass, gbufferPosView, gbufferNormView, gbufferAlbView, depthView, w, h);
        swapchainFramebuffers = VulkanPipeline.CreateFramebuffers(gpu, swapchainRenderPass);

        uint frames = (uint)GpuState.MaxFramesInFlight;
        debugVizDescPool      = VulkanDescriptors.CreatePool(gpu, frames * 4, 1);
        debugVizPositionSets  = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferPosView, sampler);
        debugVizNormalSets    = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferNormView, sampler);
        debugVizAlbedoSets    = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, gbufferAlbView, sampler);
        debugVizDepthSets     = VulkanDescriptors.AllocateSets(gpu, debugVizDescPool, singleDsLayout, frames, depthView, sampler,
            ImageLayout.DepthStencilReadOnlyOptimal);

        camera = FreeCameraController.ToCamera(cameraState, (float)w / h);
    }

    public void HandleInput(CameraInput input)
    {
        cameraState = FreeCameraController.Update(cameraState, input);
        camera = FreeCameraController.ToCamera(cameraState,
            (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height);
    }

    public void DrawDebugUi()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 30), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 60), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("G-Buffer Visualization"))
        {
            int idx = Array.IndexOf(Modes, vizMode);
            if (idx < 0) idx = 0;
            if (ImGui.Combo("Buffer", ref idx, ModeNames, ModeNames.Length))
                vizMode = Modes[idx];
        }
        ImGui.End();

        FreeCameraDebugMenu(ref cameraState);
        camera = FreeCameraController.ToCamera(cameraState,
            (float)gpu.SwapchainExtent.Width / gpu.SwapchainExtent.Height);
    }

    static void FreeCameraDebugMenu(ref FreeCameraState state)
    {
        ImGui.SetNextWindowPos(new Vector2(10, 100), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(280, 100), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Camera"))
        {
            ImGui.End();
            return;
        }
        var pos = state.Position;
        if (ImGui.DragFloat3("Position", ref pos, 0.05f))
            state = state with { Position = pos };
        var yaw = state.Yaw * 180f / MathF.PI;
        if (ImGui.SliderFloat("Yaw", ref yaw, -180, 180))
            state = state with { Yaw = yaw * MathF.PI / 180f };
        var pitch = state.Pitch * 180f / MathF.PI;
        if (ImGui.SliderFloat("Pitch", ref pitch, -89, 89))
            state = state with { Pitch = pitch * MathF.PI / 180f };
        ImGui.End();
    }

    public void RecordFrame(GpuState _, CommandBuffer cb, Scene? __, UiModel? ___, double ____, uint imageIndex)
    {
        RecordGBufferPass(cb);
        InsertGBufferBarriers(cb);
        RecordDebugVizPass(cb, imageIndex);
    }

    void RecordGBufferPass(CommandBuffer cb)
    {
        var resources = new GBufferPassResources(
            RenderPass: gbufferRenderPass,
            Framebuffer: gbufferFramebuffer,
            Pipeline: gbufferPipeline,
            PipelineLayout: gbufferPipelineLayout,
            Extent: gpu.SwapchainExtent);

        var pc = GBufferPass.BuildPushConstants(Transform.Default, camera, MaterialParams.Default);
        var matSet = materials.GetOrAllocate(TextureId.None);
        GBufferPass.Record(gpu.Vk, cb, resources, pc, matSet, vertexBuffer, indexBuffer, indexCount);
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

    unsafe void RecordDebugVizPass(CommandBuffer cb, uint imageIndex)
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
            RenderPass: swapchainRenderPass,
            Framebuffer: swapchainFramebuffers[imageIndex],
            Pipeline: debugVizPipeline,
            PipelineLayout: debugVizPipelineLayout,
            SourceSet: sourceSet,
            Extent: gpu.SwapchainExtent);
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
        if (swapchainFramebuffers.Length > 0)
            VulkanPipeline.DestroyFramebuffers(gpu, swapchainFramebuffers);
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
        swapchainFramebuffers = [];
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
        gpu.Vk.DestroySampler(gpu.Device, sampler, null);
        gpu.Vk.DestroyPipeline(gpu.Device, gbufferPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, gbufferPipelineLayout, null);
        gpu.Vk.DestroyRenderPass(gpu.Device, gbufferRenderPass, null);
        gpu.Vk.DestroyPipeline(gpu.Device, debugVizPipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, debugVizPipelineLayout, null);
        gpu.Vk.DestroyRenderPass(gpu.Device, swapchainRenderPass, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, singleDsLayout, null);
        gpu.Vk.DestroyDescriptorSetLayout(gpu.Device, materialDsLayout, null);
        materials.Dispose();
        VulkanBuffer.Destroy(gpu, vertexBuffer, vertexAlloc);
        VulkanBuffer.Destroy(gpu, indexBuffer, indexAlloc);
    }
}
