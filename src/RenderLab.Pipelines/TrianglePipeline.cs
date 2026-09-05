using System.Collections.Immutable;
using System.Numerics;
using Silk.NET.Vulkan;
using RenderLab.Gpu;
using RenderLab.Ui;
using Buffer = Silk.NET.Vulkan.Buffer;
using Framebuffer = Silk.NET.Vulkan.Framebuffer;

namespace RenderLab.Pipelines;

using Scene = RenderLab.Scene.Scene;

// ─── Minimal Modern Rendering Pipeline ──────────────────────────────
// Matches blog post 2: "From Nothing to a Triangle."
//
// Demonstrates the eight concepts needed to put a single triangle on
// screen with a modern GPU API. The pipeline only owns the pedagogical
// triangle pass; the Application layers ImGui on top via its own overlay
// render pass.

/// <summary>
/// The triangle pipeline — single render pass, single draw, three vertices.
/// Does not consume a scene; ignores the scene/UI arguments to
/// <see cref="RecordFrame"/>.
/// </summary>
public sealed class TrianglePipeline : IPipeline
{
    public string Id => "triangle";
    public bool ConsumesScenes => false;

    /// <summary>
    /// None. It draws one hard-coded triangle into the viewport: there are no G-buffer
    /// attachments to choose between, so the Visualization panel has nothing to offer for it.
    /// </summary>
    public ImmutableArray<VisualizationMode> SupportedVisualizations => [];

    GpuState gpu = null!;
    RenderPass renderPass;
    Pipeline pipeline;
    PipelineLayout pipelineLayout;
    Buffer vertexBuffer;
    Allocation vertexAlloc;
    Framebuffer framebuffer;
    Extent2D extent;

    public void Initialize(GpuState gpuState, RenderLab.Gpu.Assets.AssetRegistry _)
    {
        gpu = gpuState;

        Console.WriteLine("RenderLab — Minimal Pipeline (Triangle)");

        renderPass = VulkanPipeline.CreateViewportRenderPass(gpu);
        BuildPipelines();

        ReadOnlySpan<Vertex> vertices =
        [
            new(new Vector2(-0.5f, -0.5f), new Vector3(1f, 0f, 0f)),
            new(new Vector2( 0.5f, -0.5f), new Vector3(0f, 1f, 0f)),
            new(new Vector2( 0.0f,  0.5f), new Vector3(0f, 0f, 1f)),
        ];
        (vertexBuffer, vertexAlloc) = VulkanBuffer.Create<Vertex>(
            gpu, BufferUsageFlags.VertexBufferBit, vertices);

        Console.WriteLine("  Vertices: 3 (RGB triangle)");
    }

    unsafe void BuildPipelines()
    {
        var shaderDir = Path.Combine(AppContext.BaseDirectory, "shaders");
        var vertModule = VulkanPipeline.CreateShaderModule(gpu,
            File.ReadAllBytes(Path.Combine(shaderDir, "triangle.vert.spv")));
        var fragModule = VulkanPipeline.CreateShaderModule(gpu,
            File.ReadAllBytes(Path.Combine(shaderDir, "triangle.frag.spv")));

        pipeline = VulkanPipeline.CreateGraphicsPipeline(
            gpu, renderPass, vertModule, fragModule, out pipelineLayout);

        gpu.Vk.DestroyShaderModule(gpu.Device, vertModule, null);
        gpu.Vk.DestroyShaderModule(gpu.Device, fragModule, null);
    }

    public unsafe void ReloadShaders(GpuState _)
    {
        gpu.Vk.DestroyPipeline(gpu.Device, pipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, pipelineLayout, null);
        BuildPipelines();
    }

    public unsafe void RecreateTransient(GpuState _, RenderTarget target)
    {
        if (framebuffer.Handle != 0)
            gpu.Vk.DestroyFramebuffer(gpu.Device, framebuffer, null);

        extent = target.Extent;
        framebuffer = VulkanPipeline.CreateOffscreenFramebuffer(
            gpu, renderPass, target.View, extent.Width, extent.Height);
    }

    public unsafe void RecordFrame(GpuState gpuState, CommandBuffer cmd, Scene? _, UiModel __, double ___, RenderTarget target)
    {
        var clearColor = new ClearValue(new ClearColorValue(0.1f, 0.1f, 0.1f, 1.0f));
        var begin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), extent),
            ClearValueCount = 1,
            PClearValues = &clearColor,
        };
        gpu.Vk.CmdBeginRenderPass(cmd, &begin, SubpassContents.Inline);
        gpu.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);

        var viewport = new Viewport(0, 0, extent.Width, extent.Height, 0, 1);
        gpu.Vk.CmdSetViewport(cmd, 0, 1, &viewport);
        var scissor = new Rect2D(new Offset2D(0, 0), extent);
        gpu.Vk.CmdSetScissor(cmd, 0, 1, &scissor);

        var vb = vertexBuffer;
        ulong offset = 0;
        gpu.Vk.CmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);
        gpu.Vk.CmdDraw(cmd, 3, 1, 0, 0);
        gpu.Vk.CmdEndRenderPass(cmd);
    }

    public unsafe void Dispose()
    {
        gpu.Vk.DeviceWaitIdle(gpu.Device);
        if (framebuffer.Handle != 0)
            gpu.Vk.DestroyFramebuffer(gpu.Device, framebuffer, null);
        gpu.Vk.DestroyPipeline(gpu.Device, pipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, pipelineLayout, null);
        gpu.Vk.DestroyRenderPass(gpu.Device, renderPass, null);
        VulkanBuffer.Destroy(gpu, vertexBuffer, vertexAlloc);
    }
}
