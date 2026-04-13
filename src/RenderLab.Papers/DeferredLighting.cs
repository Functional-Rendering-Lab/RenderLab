using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using RenderLab.Gpu;
using RenderLab.Scene;

namespace RenderLab.Papers;

/// <summary>
/// Deferred Blinn-Phong lighting pass. The pure half maps an immutable
/// <see cref="Camera"/> + <see cref="PointLight"/> into the push-constant
/// layout consumed by <c>lighting.frag</c>; the impure half records the
/// fullscreen draw against a pre-built set of Vulkan resources.
/// </summary>
public static class DeferredLighting
{
    public static LightingPushConstants BuildPushConstants(Camera camera, PointLight light) => new()
    {
        CameraPos = new Vector4(camera.Position, 1f),
        LightPos = new Vector4(light.Position, 1f),
        LightColor = new Vector4(light.Color, light.Intensity),
    };

    public static unsafe void Record(
        Vk vk,
        CommandBuffer cb,
        LightingPassResources r,
        LightingPushConstants pc)
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

        var ds = r.GBufferDescriptorSet;
        vk.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, r.PipelineLayout,
            0, 1, &ds, 0, null);

        vk.CmdPushConstants(cb, r.PipelineLayout, ShaderStageFlags.FragmentBit,
            0, (uint)Marshal.SizeOf<LightingPushConstants>(), &pc);

        vk.CmdDraw(cb, 3, 1, 0, 0);

        vk.CmdEndRenderPass(cb);
    }
}

/// <summary>
/// Vulkan handles the lighting pass needs at record time. Built once per
/// resize and passed by value into <see cref="DeferredLighting.Record"/>.
/// </summary>
public readonly record struct LightingPassResources(
    RenderPass RenderPass,
    Framebuffer Framebuffer,
    Pipeline Pipeline,
    PipelineLayout PipelineLayout,
    DescriptorSet GBufferDescriptorSet,
    Extent2D Extent);
