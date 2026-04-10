using System.Collections.Immutable;
using RenderLab.Graph;
using Silk.NET.Vulkan;
using Image = Silk.NET.Vulkan.Image;

namespace RenderLab.Gpu;

/// <summary>
/// Bridges the pure render graph to the Vulkan command buffer.
/// Iterates resolved passes in order, inserts computed pipeline barriers,
/// then invokes each pass's recorder function.
/// </summary>
public static class VulkanGraphExecutor
{
    /// <summary>
    /// Executes compiled render graph passes by inserting barriers and calling recorders.
    /// </summary>
    /// <param name="state">GPU state for Vulkan API access.</param>
    /// <param name="cmd">Active command buffer (between BeginFrame/EndFrame).</param>
    /// <param name="resolvedPasses">Topologically sorted passes from <see cref="RenderGraphCompiler"/>.</param>
    /// <param name="passRecorders">Maps pass name to its recording function. Each recorder
    /// begins a render pass, binds pipeline/resources, records draw calls, and ends the render pass.</param>
    /// <param name="resourceImages">Maps logical <see cref="ResourceName"/> to the actual Vulkan
    /// <see cref="Image"/> for barrier insertion. Must include all resources referenced by barriers.</param>
    public static unsafe void Execute(
        GpuState state,
        CommandBuffer cmd,
        ImmutableArray<ResolvedPass> resolvedPasses,
        Dictionary<string, Action<Vk, CommandBuffer>> passRecorders,
        Dictionary<ResourceName, Image> resourceImages)
    {
        var vk = state.Vk;

        foreach (var resolved in resolvedPasses)
        {
            // Insert barriers
            foreach (var barrier in resolved.BarriersBefore)
            {
                if (!resourceImages.TryGetValue(barrier.Resource, out var image))
                    continue;

                var (srcStage, srcAccess, oldLayout) = MapUsage(barrier.FromUsage);
                var (dstStage, dstAccess, newLayout) = MapUsage(barrier.ToUsage);

                var imageBarrier = new ImageMemoryBarrier
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
                    0, 0, null, 0, null, 1, &imageBarrier);
            }

            // Execute pass recording
            if (passRecorders.TryGetValue(resolved.Declaration.Name, out var recorder))
                recorder(vk, cmd);
        }
    }

    private static (PipelineStageFlags stage, AccessFlags access, ImageLayout layout) MapUsage(
        ResourceUsage usage) => usage switch
    {
        ResourceUsage.ColorAttachmentWrite => (
            PipelineStageFlags.ColorAttachmentOutputBit,
            AccessFlags.ColorAttachmentWriteBit,
            ImageLayout.ColorAttachmentOptimal),

        ResourceUsage.DepthStencilWrite => (
            PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
            AccessFlags.DepthStencilAttachmentWriteBit,
            ImageLayout.DepthStencilAttachmentOptimal),

        ResourceUsage.ShaderRead => (
            PipelineStageFlags.FragmentShaderBit,
            AccessFlags.ShaderReadBit,
            ImageLayout.ShaderReadOnlyOptimal),

        ResourceUsage.Present => (
            PipelineStageFlags.BottomOfPipeBit,
            AccessFlags.None,
            ImageLayout.PresentSrcKhr),

        _ => throw new ArgumentOutOfRangeException(nameof(usage), usage, null),
    };
}
