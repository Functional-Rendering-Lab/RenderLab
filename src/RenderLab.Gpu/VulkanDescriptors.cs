using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

/// <summary>
/// Creates descriptor set layouts, pools, and allocated sets for binding
/// sampled images to fragment shaders. Supports single-sampler (tonemap)
/// and multi-sampler (GBuffer lighting) configurations.
/// </summary>
public static class VulkanDescriptors
{
    /// <summary>
    /// Creates a layout with a single combined image sampler at binding 0 (fragment stage).
    /// Used by the tonemap pass to read the HDR image.
    /// </summary>
    public static unsafe DescriptorSetLayout CreateSamplerLayout(GpuState state)
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit,
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding,
        };

        if (state.Vk.CreateDescriptorSetLayout(state.Device, &layoutInfo, null, out var layout) != Result.Success)
            throw new InvalidOperationException("Failed to create descriptor set layout.");

        return layout;
    }

    /// <summary>Creates a descriptor pool sized for <paramref name="maxSets"/> sets with 1 sampler each.</summary>
    public static unsafe DescriptorPool CreatePool(GpuState state, uint maxSets)
    {
        var poolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = maxSets,
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = maxSets,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };

        if (state.Vk.CreateDescriptorPool(state.Device, &poolInfo, null, out var pool) != Result.Success)
            throw new InvalidOperationException("Failed to create descriptor pool.");

        return pool;
    }

    /// <summary>
    /// Allocates <paramref name="count"/> descriptor sets (one per frame-in-flight),
    /// each pointing to the same <paramref name="imageView"/> + <paramref name="sampler"/>.
    /// </summary>
    public static unsafe DescriptorSet[] AllocateSets(
        GpuState state, DescriptorPool pool, DescriptorSetLayout layout, uint count,
        ImageView imageView, Sampler sampler,
        ImageLayout imageLayout = ImageLayout.ShaderReadOnlyOptimal)
    {
        var layouts = new DescriptorSetLayout[count];
        Array.Fill(layouts, layout);

        var sets = new DescriptorSet[count];

        fixed (DescriptorSetLayout* pLayouts = layouts)
        fixed (DescriptorSet* pSets = sets)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = count,
                PSetLayouts = pLayouts,
            };

            if (state.Vk.AllocateDescriptorSets(state.Device, &allocInfo, pSets) != Result.Success)
                throw new InvalidOperationException("Failed to allocate descriptor sets.");
        }

        // Write same image+sampler to all sets
        for (int i = 0; i < count; i++)
        {
            var imageInfo = new DescriptorImageInfo
            {
                ImageLayout = imageLayout,
                ImageView = imageView,
                Sampler = sampler,
            };

            var write = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = sets[i],
                DstBinding = 0,
                DstArrayElement = 0,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                PImageInfo = &imageInfo,
            };

            state.Vk.UpdateDescriptorSets(state.Device, 1, &write, 0, null);
        }

        return sets;
    }

    /// <summary>
    /// Creates a layout with 3 combined image samplers at bindings 0-2 (fragment stage).
    /// Used by the lighting pass to read GBuffer position, normal, and albedo textures.
    /// </summary>
    public static unsafe DescriptorSetLayout CreateGBufferSamplerLayout(GpuState state)
    {
        var bindings = stackalloc DescriptorSetLayoutBinding[3];
        for (uint i = 0; i < 3; i++)
        {
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = i,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = bindings,
        };

        if (state.Vk.CreateDescriptorSetLayout(state.Device, &layoutInfo, null, out var layout) != Result.Success)
            throw new InvalidOperationException("Failed to create GBuffer descriptor set layout.");

        return layout;
    }

    /// <summary>Creates a descriptor pool sized for <paramref name="maxSets"/> sets with <paramref name="samplerCount"/> samplers each.</summary>
    public static unsafe DescriptorPool CreatePool(GpuState state, uint maxSets, uint samplerCount)
    {
        var poolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = maxSets * samplerCount,
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = maxSets,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };

        if (state.Vk.CreateDescriptorPool(state.Device, &poolInfo, null, out var pool) != Result.Success)
            throw new InvalidOperationException("Failed to create descriptor pool.");

        return pool;
    }

    /// <summary>
    /// Allocates <paramref name="count"/> descriptor sets for GBuffer sampling.
    /// Binds position (0), normal (1), and albedo (2) image views with the shared sampler.
    /// </summary>
    public static unsafe DescriptorSet[] AllocateGBufferSets(
        GpuState state, DescriptorPool pool, DescriptorSetLayout layout, uint count,
        ImageView posView, ImageView normalView, ImageView albedoView, Sampler sampler)
    {
        var layouts = new DescriptorSetLayout[count];
        Array.Fill(layouts, layout);

        var sets = new DescriptorSet[count];

        fixed (DescriptorSetLayout* pLayouts = layouts)
        fixed (DescriptorSet* pSets = sets)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = count,
                PSetLayouts = pLayouts,
            };

            if (state.Vk.AllocateDescriptorSets(state.Device, &allocInfo, pSets) != Result.Success)
                throw new InvalidOperationException("Failed to allocate GBuffer descriptor sets.");
        }

        var views = new[] { posView, normalView, albedoView };
        var imageInfos = stackalloc DescriptorImageInfo[3];

        for (int i = 0; i < count; i++)
        {
            var writes = new WriteDescriptorSet[3];

            for (int b = 0; b < 3; b++)
            {
                imageInfos[b] = new DescriptorImageInfo
                {
                    ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                    ImageView = views[b],
                    Sampler = sampler,
                };

                writes[b] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = sets[i],
                    DstBinding = (uint)b,
                    DstArrayElement = 0,
                    DescriptorType = DescriptorType.CombinedImageSampler,
                    DescriptorCount = 1,
                    PImageInfo = &imageInfos[b],
                };
            }

            fixed (WriteDescriptorSet* pWrites = writes)
                state.Vk.UpdateDescriptorSets(state.Device, 3, pWrites, 0, null);
        }

        return sets;
    }
}
