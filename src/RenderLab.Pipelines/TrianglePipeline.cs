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

    GpuState gpu = null!;
    RenderPass renderPass;
    Pipeline pipeline;
    PipelineLayout pipelineLayout;
    Buffer vertexBuffer;
    Allocation vertexAlloc;
    Framebuffer[] framebuffers = [];

    public void Initialize(GpuState gpuState, RenderLab.Gpu.Assets.AssetRegistry _, RenderPass overlayRenderPass)
    {
        gpu = gpuState;

        Console.WriteLine("RenderLab — Minimal Pipeline (Triangle)");
        Console.WriteLine($"  Swapchain: {gpu.SwapchainExtent.Width}x{gpu.SwapchainExtent.Height}");

        renderPass = VulkanPipeline.CreateRenderPass(gpu);
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

    public void RecreateTransient(GpuState _)
    {
        if (framebuffers.Length > 0)
            VulkanPipeline.DestroyFramebuffers(gpu, framebuffers);
        framebuffers = VulkanPipeline.CreateFramebuffers(gpu, renderPass);
    }

    public unsafe void RecordFrame(GpuState gpuState, CommandBuffer cmd, Scene? _, UiModel? __, double ___, uint imageIndex)
    {
        var clearColor = new ClearValue(new ClearColorValue(0.1f, 0.1f, 0.1f, 1.0f));
        var begin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = framebuffers[imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent),
            ClearValueCount = 1,
            PClearValues = &clearColor,
        };
        gpu.Vk.CmdBeginRenderPass(cmd, &begin, SubpassContents.Inline);
        gpu.Vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);

        var viewport = new Viewport(0, 0, gpu.SwapchainExtent.Width, gpu.SwapchainExtent.Height, 0, 1);
        gpu.Vk.CmdSetViewport(cmd, 0, 1, &viewport);
        var scissor = new Rect2D(new Offset2D(0, 0), gpu.SwapchainExtent);
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
        if (framebuffers.Length > 0)
            VulkanPipeline.DestroyFramebuffers(gpu, framebuffers);
        gpu.Vk.DestroyPipeline(gpu.Device, pipeline, null);
        gpu.Vk.DestroyPipelineLayout(gpu.Device, pipelineLayout, null);
        gpu.Vk.DestroyRenderPass(gpu.Device, renderPass, null);
        VulkanBuffer.Destroy(gpu, vertexBuffer, vertexAlloc);
    }
}
