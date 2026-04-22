using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

/// <summary>
/// Immutable snapshot of physical device properties and features, queried once
/// during <see cref="VulkanDevice.Create"/>. Papers and passes read this record
/// instead of calling Vulkan directly for capability queries.
/// </summary>
/// <param name="DeviceName">Human-readable GPU name from <c>VkPhysicalDeviceProperties.deviceName</c>.</param>
/// <param name="ApiVersion">Vulkan API version requested at instance creation (e.g. <c>Vk.Version13</c>).</param>
/// <param name="DepthFormat">Best supported depth format. Prefers D32_SFLOAT.</param>
/// <param name="TimestampPeriod">Nanoseconds per GPU timestamp tick. Used by <see cref="Debug.GpuTimestamps"/> to convert ticks to milliseconds.</param>
/// <param name="TimestampSupported">Whether the device supports timestamp queries on graphics/compute queues.</param>
/// <param name="MaxColorAttachments">Maximum number of simultaneous color attachments per subpass. GBuffer uses 3.</param>
/// <param name="MaxBoundDescriptorSets">Maximum descriptor sets that can be bound simultaneously.</param>
/// <param name="MaxSamplersPerStage">Maximum combined image samplers per shader stage. Lighting pass binds 3.</param>
/// <param name="MaxPushConstantSize">Maximum push constant block size in bytes. GBuffer uses 128 (two Matrix4x4).</param>
/// <param name="MaxComputeWorkGroupSize">Maximum total invocations in a single compute work group.</param>
/// <param name="SupportsGeometryShader">Whether geometry shaders are available.</param>
/// <param name="SupportsTessellation">Whether tessellation shaders are available.</param>
public sealed record DeviceCapabilities(
    string DeviceName,
    uint ApiVersion,
    Format DepthFormat,
    float TimestampPeriod,
    bool TimestampSupported,
    uint MaxColorAttachments,
    uint MaxBoundDescriptorSets,
    uint MaxSamplersPerStage,
    uint MaxPushConstantSize,
    uint MaxComputeWorkGroupSize,
    bool SupportsGeometryShader,
    bool SupportsTessellation);
