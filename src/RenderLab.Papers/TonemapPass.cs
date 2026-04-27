using Silk.NET.Vulkan;

namespace RenderLab.Papers;

/// <summary>
/// Tonemap pass: samples the HDR image and writes the swapchain backbuffer
/// via a fullscreen triangle. No push constants — the tonemap operator is
/// fixed in the fragment shader.
/// </summary>
public static class TonemapPass
{
    public static unsafe void Record(
        Vk vk,
        CommandBuffer cb,
        TonemapPassResources r)
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

        var ds = r.HdrSet;
        vk.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, r.PipelineLayout,
            0, 1, &ds, 0, null);

        vk.CmdDraw(cb, 3, 1, 0, 0);

        vk.CmdEndRenderPass(cb);
    }
}

/// <summary>
/// Vulkan handles the tonemap pass needs at record time. Built once per
/// resize and passed by value into <see cref="TonemapPass.Record"/>.
/// </summary>
public readonly record struct TonemapPassResources(
    RenderPass RenderPass,
    Framebuffer Framebuffer,
    Pipeline Pipeline,
    PipelineLayout PipelineLayout,
    DescriptorSet HdrSet,
    Extent2D Extent);
