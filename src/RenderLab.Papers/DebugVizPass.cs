using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using RenderLab.Gpu;
using RenderLab.Scene;

namespace RenderLab.Papers;

/// <summary>
/// Debug visualization pass: samples a single source image (a G-Buffer
/// attachment, depth, or HDR) and writes it to the swapchain backbuffer
/// via a fullscreen triangle. The pure half maps the active mode and the
/// camera's near/far planes into the push-constant block; the demo owns
/// the descriptor-set selection because it owns the descriptor arrays.
/// </summary>
public static class DebugVizPass
{
    public static DebugVizPushConstants BuildPushConstants(bool depthMode, Camera camera) => new()
    {
        Mode = depthMode ? 1 : 0,
        NearPlane = camera.NearPlane,
        FarPlane = camera.FarPlane,
    };

    public static unsafe void Record(
        Vk vk,
        CommandBuffer cb,
        DebugVizPassResources r,
        DebugVizPushConstants pc)
    {
        var clearValue = new ClearValue(new ClearColorValue(0, 0, 0, 1));

        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = r.RenderPass,
            Framebuffer = r.Framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), r.Extent),
            ClearValueCount = 1,
            PClearValues = &clearValue,
        };

        vk.CmdBeginRenderPass(cb, &renderPassBegin, SubpassContents.Inline);
        vk.CmdBindPipeline(cb, PipelineBindPoint.Graphics, r.Pipeline);

        var viewport = new Viewport(0, 0, r.Extent.Width, r.Extent.Height, 0, 1);
        vk.CmdSetViewport(cb, 0, 1, &viewport);

        var scissor = new Rect2D(new Offset2D(0, 0), r.Extent);
        vk.CmdSetScissor(cb, 0, 1, &scissor);

        var ds = r.SourceSet;
        vk.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, r.PipelineLayout,
            0, 1, &ds, 0, null);

        vk.CmdPushConstants(cb, r.PipelineLayout, ShaderStageFlags.FragmentBit,
            0, (uint)Marshal.SizeOf<DebugVizPushConstants>(), &pc);

        vk.CmdDraw(cb, 3, 1, 0, 0);

        vk.CmdEndRenderPass(cb);
    }
}

/// <summary>
/// Vulkan handles the debug-viz pass needs at record time. The descriptor
/// set is chosen per frame by the caller from its bank of source-image
/// descriptor sets.
/// </summary>
public readonly record struct DebugVizPassResources(
    RenderPass RenderPass,
    Framebuffer Framebuffer,
    Pipeline Pipeline,
    PipelineLayout PipelineLayout,
    DescriptorSet SourceSet,
    Extent2D Extent);
