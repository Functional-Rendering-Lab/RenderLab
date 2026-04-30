using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using RenderLab.Gpu;
using RenderLab.Scene;

namespace RenderLab.Papers;

/// <summary>
/// Deferred lighting pass with selectable shading model (Lambertian, Phong,
/// Blinn-Phong). The pure half maps an immutable <see cref="Camera"/>, light
/// count, and <see cref="ShadingMode"/> into the push-constant layout consumed
/// by <c>lighting.frag</c>; per-light data lives in an SSBO (set 1, binding 0)
/// packed by <see cref="LightPacking"/>. The impure half records the fullscreen
/// draw against a pre-built set of Vulkan resources.
/// </summary>
public static class DeferredLighting
{
    public static LightingPushConstants BuildPushConstants(
        Camera camera,
        int lightCount,
        ShadingMode mode,
        HemisphericAmbient ambient,
        bool lightingOnly = false) => new()
    {
        CameraPos = new Vector4(camera.Position, 1f),
        ShadingMode = (int)mode,
        LightingOnly = lightingOnly ? 1 : 0,
        LightCount = lightCount,
        Pad0 = 0,
        AmbientSky = new Vector4(ambient.Sky, 0f),
        AmbientGround = new Vector4(ambient.Ground, 0f),
    };

    public static unsafe void Record(
        Vk vk,
        CommandBuffer cb,
        LightingPassResources r,
        LightingPushConstants pc,
        Vector3 clearColor)
    {
        var clearValue = new ClearValue(new ClearColorValue(clearColor.X, clearColor.Y, clearColor.Z, 1));

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

        var sets = stackalloc DescriptorSet[2] { r.GBufferDescriptorSet, r.LightDescriptorSet };
        vk.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, r.PipelineLayout,
            0, 2, sets, 0, null);

        vk.CmdPushConstants(cb, r.PipelineLayout, ShaderStageFlags.FragmentBit,
            0, (uint)Marshal.SizeOf<LightingPushConstants>(), &pc);

        vk.CmdDraw(cb, 3, 1, 0, 0);

        vk.CmdEndRenderPass(cb);
    }
}

/// <summary>
/// Vulkan handles the lighting pass needs at record time. Built once per
/// resize and passed by value into <see cref="DeferredLighting.Record"/>.
/// <see cref="LightDescriptorSet"/> points at the active frame's SSBO of
/// packed <see cref="GpuLight"/> entries.
/// </summary>
public readonly record struct LightingPassResources(
    RenderPass RenderPass,
    Framebuffer Framebuffer,
    Pipeline Pipeline,
    PipelineLayout PipelineLayout,
    DescriptorSet GBufferDescriptorSet,
    DescriptorSet LightDescriptorSet,
    Extent2D Extent);
