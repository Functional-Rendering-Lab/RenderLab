using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using RenderLab.Assets;
using RenderLab.Gpu;
using RenderLab.Gpu.Assets;
using RenderLab.Scene;
using Buffer = Silk.NET.Vulkan.Buffer;

using RenderLab.Functional;

namespace RenderLab.Papers;

/// <summary>
/// G-Buffer geometry pass. The pure half builds the push-constant block
/// from an immutable scene snapshot; the impure half records a single
/// indexed mesh into the pre-built G-Buffer render pass and framebuffer.
/// </summary>
public static class GBufferPass
{
    public static GBufferPushConstants BuildPushConstants(
        Transform mesh, MaterialParams material) => new()
    {
        Model = mesh.Matrix,
        Albedo = material.Albedo,
        SpecularStrength = material.SpecularStrength,
        Shininess = material.Shininess,
    };

    /// <summary>
    /// Translates a <see cref="BlinnPhongMaterial"/> asset into the GBuffer's
    /// flat parameter struct. Other material kinds need their own conversion
    /// (or fall through to the default). Centralised here so paper code keeps
    /// the asset-shape dependency, not the rest of the engine.
    /// </summary>
    public static MaterialParams ToParams(MaterialAsset asset) => asset switch
    {
        BlinnPhongMaterial bp => new MaterialParams(
            Color01.UnsafeFrom(bp.Albedo),
            UnitInterval.UnsafeFrom(Math.Clamp(bp.SpecularStrength, 0f, 1f)),
            Shininess.UnsafeFrom(Math.Clamp(bp.Shininess, 0f, MaterialParams.ShininessRange))),
        _                     => MaterialParams.Default,
    };

    public static unsafe void Record(
        Vk vk,
        CommandBuffer cb,
        GBufferPassResources r,
        ImmutableArray<Drawable> drawables,
        IAssetCatalog catalog,
        IGpuAssetResolver resolver,
        MaterialDescriptors materials,
        DescriptorSet cameraSet)
    {
        BeginPass(vk, cb, r);

        vk.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, r.PipelineLayout,
            1, 1, &cameraSet, 0, null);

        foreach (var d in drawables)
        {
            var asset = catalog.GetMaterial(d.Material);
            var albedoMap = asset is BlinnPhongMaterial bp ? bp.AlbedoMap.ValueOr(TextureId.None) : TextureId.None;
            var pc = BuildPushConstants(d.Transform, ToParams(asset));
            vk.CmdPushConstants(cb, r.PipelineLayout,
                ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                0, (uint)Marshal.SizeOf<GBufferPushConstants>(), &pc);

            var matSet = materials.GetOrAllocate(albedoMap);
            vk.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, r.PipelineLayout,
                0, 1, &matSet, 0, null);

            var h = resolver.ResolveMesh(d.Mesh);
            var vb = h.VertexBuffer;
            ulong offset = 0;
            vk.CmdBindVertexBuffers(cb, 0, 1, &vb, &offset);
            vk.CmdBindIndexBuffer(cb, h.IndexBuffer, 0, IndexType.Uint32);
            vk.CmdDrawIndexed(cb, h.IndexCount, 1, 0, 0, 0);
        }

        vk.CmdEndRenderPass(cb);
    }

    public static unsafe void Record(
        Vk vk,
        CommandBuffer cb,
        GBufferPassResources r,
        GBufferPushConstants pc,
        DescriptorSet materialSet,
        DescriptorSet cameraSet,
        Buffer vertexBuffer,
        Buffer indexBuffer,
        uint indexCount)
    {
        BeginPass(vk, cb, r);

        vk.CmdPushConstants(cb, r.PipelineLayout,
            ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            0, (uint)Marshal.SizeOf<GBufferPushConstants>(), &pc);

        var sets = stackalloc DescriptorSet[2] { materialSet, cameraSet };
        vk.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, r.PipelineLayout,
            0, 2, sets, 0, null);

        var vb = vertexBuffer;
        ulong offset = 0;
        vk.CmdBindVertexBuffers(cb, 0, 1, &vb, &offset);
        vk.CmdBindIndexBuffer(cb, indexBuffer, 0, IndexType.Uint32);
        vk.CmdDrawIndexed(cb, indexCount, 1, 0, 0, 0);

        vk.CmdEndRenderPass(cb);
    }

    static unsafe void BeginPass(Vk vk, CommandBuffer cb, GBufferPassResources r)
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
