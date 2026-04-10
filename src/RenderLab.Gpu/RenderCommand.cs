using System.Numerics;
using System.Runtime.InteropServices;

namespace RenderLab.Gpu;

public enum RenderCommandTag : byte
{
    ClearColor,
    ClearDepth,
    SetPipeline,
    SetVertexBuffer,
    SetIndexBuffer,
    SetDescriptorSet,
    PushConstants,
    DrawIndexed,
    Dispatch,
    CopyBufferToImage,
    Blit,
}

/// <summary>
/// Tagged value-type render command (discriminated union via struct + tag byte).
/// Zero heap allocation, cache-friendly, Span-compatible. Factory methods create
/// each variant; <see cref="Match{TResult}"/> enforces exhaustive handling of all cases.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct RenderCommand
{
    public RenderCommandTag Tag { get; init; }

    // ClearColor
    public Vector4 ClearColorValue { get; init; }
    public uint AttachmentIndex { get; init; }

    // ClearDepth
    public float ClearDepthValue { get; init; }

    // SetPipeline
    public PipelineHandle Pipeline { get; init; }

    // SetVertexBuffer / SetIndexBuffer
    public BufferHandle Buffer { get; init; }
    public uint Slot { get; init; }

    // SetDescriptorSet
    public DescriptorSetHandle DescriptorSet { get; init; }
    public uint SetIndex { get; init; }

    // DrawIndexed
    public uint IndexCount { get; init; }
    public uint InstanceCount { get; init; }
    public uint FirstIndex { get; init; }
    public int VertexOffset { get; init; }

    // Dispatch
    public uint GroupCountX { get; init; }
    public uint GroupCountY { get; init; }
    public uint GroupCountZ { get; init; }

    // CopyBufferToImage
    public BufferHandle SrcBuffer { get; init; }
    public ImageHandle DstImage { get; init; }

    // Blit
    public ImageHandle SrcImage { get; init; }
    public ImageHandle BlitDstImage { get; init; }

    public static RenderCommand CreateClearColor(Vector4 color, uint attachment = 0) => new()
    {
        Tag = RenderCommandTag.ClearColor,
        ClearColorValue = color,
        AttachmentIndex = attachment,
    };

    public static RenderCommand CreateClearDepth(float depth = 1.0f) => new()
    {
        Tag = RenderCommandTag.ClearDepth,
        ClearDepthValue = depth,
    };

    public static RenderCommand CreateSetPipeline(PipelineHandle pipeline) => new()
    {
        Tag = RenderCommandTag.SetPipeline,
        Pipeline = pipeline,
    };

    public static RenderCommand CreateSetVertexBuffer(BufferHandle buffer, uint slot = 0) => new()
    {
        Tag = RenderCommandTag.SetVertexBuffer,
        Buffer = buffer,
        Slot = slot,
    };

    public static RenderCommand CreateSetIndexBuffer(BufferHandle buffer) => new()
    {
        Tag = RenderCommandTag.SetIndexBuffer,
        Buffer = buffer,
    };

    public static RenderCommand CreateSetDescriptorSet(DescriptorSetHandle set, uint index) => new()
    {
        Tag = RenderCommandTag.SetDescriptorSet,
        DescriptorSet = set,
        SetIndex = index,
    };

    public static RenderCommand CreateDrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0) => new()
    {
        Tag = RenderCommandTag.DrawIndexed,
        IndexCount = indexCount,
        InstanceCount = instanceCount,
        FirstIndex = firstIndex,
        VertexOffset = vertexOffset,
    };

    public static RenderCommand CreateDispatch(uint x, uint y, uint z) => new()
    {
        Tag = RenderCommandTag.Dispatch,
        GroupCountX = x,
        GroupCountY = y,
        GroupCountZ = z,
    };

    public static RenderCommand CreateCopyBufferToImage(BufferHandle src, ImageHandle dst) => new()
    {
        Tag = RenderCommandTag.CopyBufferToImage,
        SrcBuffer = src,
        DstImage = dst,
    };

    public static RenderCommand CreateBlit(ImageHandle src, ImageHandle dst) => new()
    {
        Tag = RenderCommandTag.Blit,
        SrcImage = src,
        BlitDstImage = dst,
    };

    public TResult Match<TResult>(
        Func<Vector4, uint, TResult> clearColor,
        Func<float, TResult> clearDepth,
        Func<PipelineHandle, TResult> setPipeline,
        Func<BufferHandle, uint, TResult> setVertexBuffer,
        Func<BufferHandle, TResult> setIndexBuffer,
        Func<DescriptorSetHandle, uint, TResult> setDescriptorSet,
        Func<TResult> pushConstants,
        Func<uint, uint, uint, int, TResult> drawIndexed,
        Func<uint, uint, uint, TResult> dispatch,
        Func<BufferHandle, ImageHandle, TResult> copyBufferToImage,
        Func<ImageHandle, ImageHandle, TResult> blit) => Tag switch
    {
        RenderCommandTag.ClearColor => clearColor(ClearColorValue, AttachmentIndex),
        RenderCommandTag.ClearDepth => clearDepth(ClearDepthValue),
        RenderCommandTag.SetPipeline => setPipeline(Pipeline),
        RenderCommandTag.SetVertexBuffer => setVertexBuffer(Buffer, Slot),
        RenderCommandTag.SetIndexBuffer => setIndexBuffer(Buffer),
        RenderCommandTag.SetDescriptorSet => setDescriptorSet(DescriptorSet, SetIndex),
        RenderCommandTag.PushConstants => pushConstants(),
        RenderCommandTag.DrawIndexed => drawIndexed(IndexCount, InstanceCount, FirstIndex, VertexOffset),
        RenderCommandTag.Dispatch => dispatch(GroupCountX, GroupCountY, GroupCountZ),
        RenderCommandTag.CopyBufferToImage => copyBufferToImage(SrcBuffer, DstImage),
        RenderCommandTag.Blit => blit(SrcImage, BlitDstImage),
        _ => throw new InvalidOperationException($"Unknown command tag: {Tag}"),
    };
}
