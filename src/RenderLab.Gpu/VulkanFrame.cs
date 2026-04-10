using System.Numerics;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RenderLab.Gpu;

/// <summary>
/// Per-frame Vulkan operations: acquire, record, submit, present.
/// For M0, records a simple clear-to-color via the full chain.
/// </summary>
public static class VulkanFrame
{
    public static unsafe bool BeginFrame(GpuState state, out uint imageIndex)
    {
        imageIndex = 0;
        var vk = state.Vk;
        var frame = state.CurrentFrame;

        // Wait for this frame's fence
        var fence = state.InFlightFences[frame];
        vk.WaitForFences(state.Device, 1, &fence, true, ulong.MaxValue);

        // Acquire next swapchain image
        uint imgIdx = 0;
        var result = state.KhrSwapchain.AcquireNextImage(
            state.Device, state.Swapchain, ulong.MaxValue,
            state.ImageAvailableSemaphores[frame], default, &imgIdx);
        imageIndex = imgIdx;

        if (result == Result.ErrorOutOfDateKhr)
            return false;

        if (result != Result.Success && result != Result.SuboptimalKhr)
            throw new InvalidOperationException($"Failed to acquire swapchain image: {result}");

        // Only reset fence when we know we're submitting work
        vk.ResetFences(state.Device, 1, &fence);

        // Reset and begin command buffer
        var cmd = state.CommandBuffers[frame];
        vk.ResetCommandBuffer(cmd, 0);

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
        };
        vk.BeginCommandBuffer(cmd, &beginInfo);

        return true;
    }

    public static unsafe void RecordClearScreen(GpuState state, uint imageIndex, Vector4 color)
    {
        var vk = state.Vk;
        var cmd = state.CommandBuffers[state.CurrentFrame];
        var image = state.SwapchainImages[imageIndex];

        TransitionImageLayout(vk, cmd, image,
            ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
            AccessFlags.None, AccessFlags.TransferWriteBit,
            PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit);

        var clearColor = new ClearColorValue(color.X, color.Y, color.Z, color.W);
        var range = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        vk.CmdClearColorImage(cmd, image, ImageLayout.TransferDstOptimal, &clearColor, 1, &range);

        TransitionImageLayout(vk, cmd, image,
            ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr,
            AccessFlags.TransferWriteBit, AccessFlags.None,
            PipelineStageFlags.TransferBit, PipelineStageFlags.BottomOfPipeBit);
    }

    public static unsafe void RecordDrawTriangle(
        GpuState state, uint imageIndex,
        RenderPass renderPass, Framebuffer[] framebuffers,
        Pipeline pipeline, PipelineLayout pipelineLayout,
        Silk.NET.Vulkan.Buffer vertexBuffer, Silk.NET.Vulkan.Buffer indexBuffer,
        uint indexCount, Vector4 clearColor)
    {
        var vk = state.Vk;
        var cmd = state.CommandBuffers[state.CurrentFrame];

        var clearValue = new ClearValue(new ClearColorValue(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));

        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = framebuffers[imageIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), state.SwapchainExtent),
            ClearValueCount = 1,
            PClearValues = &clearValue,
        };

        vk.CmdBeginRenderPass(cmd, &renderPassBegin, SubpassContents.Inline);

        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, pipeline);

        // Dynamic viewport + scissor
        var viewport = new Viewport(0, 0, state.SwapchainExtent.Width, state.SwapchainExtent.Height, 0, 1);
        vk.CmdSetViewport(cmd, 0, 1, &viewport);

        var scissor = new Rect2D(new Offset2D(0, 0), state.SwapchainExtent);
        vk.CmdSetScissor(cmd, 0, 1, &scissor);

        // Bind vertex buffer
        var vb = vertexBuffer;
        ulong offset = 0;
        vk.CmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);

        // Bind index buffer
        vk.CmdBindIndexBuffer(cmd, indexBuffer, 0, IndexType.Uint16);

        // Draw
        vk.CmdDrawIndexed(cmd, indexCount, 1, 0, 0, 0);

        vk.CmdEndRenderPass(cmd);
    }

    public static unsafe bool EndFrame(GpuState state, uint imageIndex)
    {
        var vk = state.Vk;
        var frame = state.CurrentFrame;
        var cmd = state.CommandBuffers[frame];

        vk.EndCommandBuffer(cmd);

        // Submit
        var waitSemaphore = state.ImageAvailableSemaphores[frame];
        var signalSemaphore = state.RenderFinishedSemaphores[imageIndex];
        var waitStage = PipelineStageFlags.ColorAttachmentOutputBit;

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore,
        };

        if (vk.QueueSubmit(state.GraphicsQueue, 1, &submitInfo, state.InFlightFences[frame]) != Result.Success)
            throw new InvalidOperationException("Failed to submit draw command buffer.");

        // Present
        var swapchain = state.Swapchain;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &signalSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex,
        };

        var result = state.KhrSwapchain.QueuePresent(state.PresentQueue, &presentInfo);

        state.CurrentFrame = (state.CurrentFrame + 1) % GpuState.MaxFramesInFlight;

        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr || state.FramebufferResized)
        {
            state.FramebufferResized = false;
            return false; // Caller should recreate swapchain
        }

        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to present: {result}");

        return true;
    }

    private static unsafe void TransitionImageLayout(
        Vk vk, CommandBuffer cmd, Image image,
        ImageLayout oldLayout, ImageLayout newLayout,
        AccessFlags srcAccess, AccessFlags dstAccess,
        PipelineStageFlags srcStage, PipelineStageFlags dstStage)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
        };

        vk.CmdPipelineBarrier(cmd, srcStage, dstStage,
            0, 0, null, 0, null, 1, &barrier);
    }
}
