namespace RenderLab.Gpu;

/// <summary>
/// Opaque, typed indices into GPU-side pools.
/// Generation counter enables use-after-free detection in debug builds.
/// </summary>
public readonly record struct BufferHandle(uint Index, uint Generation);
public readonly record struct ImageHandle(uint Index, uint Generation);
public readonly record struct SamplerHandle(uint Index, uint Generation);
public readonly record struct PipelineHandle(uint Index, uint Generation);
public readonly record struct DescriptorSetHandle(uint Index, uint Generation);
public readonly record struct ShaderModuleHandle(uint Index, uint Generation);
