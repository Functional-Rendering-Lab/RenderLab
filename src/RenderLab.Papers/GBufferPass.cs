using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using RenderLab.Gpu;
using RenderLab.Scene;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RenderLab.Papers;

/// <summary>
/// G-Buffer geometry pass. The pure half builds the push-constant block
/// from an immutable scene snapshot; the impure half records a single
/// indexed mesh into the pre-built G-Buffer render pass and framebuffer.
/// </summary>
public static class GBufferPass
{
    public static GBufferPushConstants BuildPushConstants(
        Transform mesh, Camera camera, MaterialParams material) => new()
    {
        Model = mesh.Matrix,
        ViewProj = camera.ViewProjectionMatrix,
        Albedo = material.Albedo,
        SpecularStrength = material.SpecularStrength,
        Shininess = material.Shininess,
    };

    public static unsafe void Record(
        Vk vk,
        CommandBuffer cb,
        GBufferPassResources r,
        GBufferPushConstants pc,
        Buffer vertexBuffer,
        Buffer indexBuffer,
        uint indexCount)
    {
        var clearValues = stackalloc ClearValue[4];
        clearValues[0] = new ClearValue(new ClearColorValue(0, 0, 0, 0));
        clearValues[1] = new ClearValue(new ClearColorValue(0, 0, 0, 0));
        clearValues[2] = new ClearValue(new ClearColorValue(0, 0, 0, 0));
        clearValues[3] = new ClearValue(depthStencil: new ClearDepthStencilValue(1.0f, 0));

        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = r.RenderPass,
            Framebuffer = r.Framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), r.Extent),
            ClearValueCount = 4,
            PClearValues = clearValues,
        };

        vk.CmdBeginRenderPass(cb, &renderPassBegin, SubpassContents.Inline);
        vk.CmdBindPipeline(cb, PipelineBindPoint.Graphics, r.Pipeline);

        var viewport = new Viewport(0, 0, r.Extent.Width, r.Extent.Height, 0, 1);
        vk.CmdSetViewport(cb, 0, 1, &viewport);

        var scissor = new Rect2D(new Offset2D(0, 0), r.Extent);
        vk.CmdSetScissor(cb, 0, 1, &scissor);

        vk.CmdPushConstants(cb, r.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0, (uint)Marshal.SizeOf<GBufferPushConstants>(), &pc);

        var vb = vertexBuffer;
        ulong offset = 0;
        vk.CmdBindVertexBuffers(cb, 0, 1, &vb, &offset);
        vk.CmdBindIndexBuffer(cb, indexBuffer, 0, IndexType.Uint32);
        vk.CmdDrawIndexed(cb, indexCount, 1, 0, 0, 0);

        vk.CmdEndRenderPass(cb);
    }
}

/// <summary>
/// Vulkan handles the G-Buffer pass needs at record time. Built once per
/// resize and passed by value into <see cref="GBufferPass.Record"/>.
/// </summary>
public readonly record struct GBufferPassResources(
    RenderPass RenderPass,
    Framebuffer Framebuffer,
    Pipeline Pipeline,
    PipelineLayout PipelineLayout,
    Extent2D Extent);
