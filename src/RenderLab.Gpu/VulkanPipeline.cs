using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

public static class VulkanPipeline
{
    // GBuffer formats used across render pass, image creation, and pipeline
    public const Format GBufferPositionFormat = Format.R16G16B16A16Sfloat;
    public const Format GBufferNormalFormat = Format.R16G16B16A16Sfloat;
    public const Format GBufferAlbedoFormat = Format.R8G8B8A8Unorm;
    public const Format HdrFormat = Format.R16G16B16A16Sfloat;
    public static unsafe ShaderModule CreateShaderModule(GpuState state, ReadOnlySpan<byte> spirvBytes)
    {
        fixed (byte* code = spirvBytes)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirvBytes.Length,
                PCode = (uint*)code,
            };

            if (state.Vk.CreateShaderModule(state.Device, &createInfo, null, out var module) != Result.Success)
                throw new InvalidOperationException("Failed to create shader module.");

            return module;
        }
    }

    public static unsafe RenderPass CreateRenderPass(GpuState state)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = state.SwapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr,
        };

        var colorRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal,
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };

        if (state.Vk.CreateRenderPass(state.Device, &renderPassInfo, null, out var renderPass) != Result.Success)
            throw new InvalidOperationException("Failed to create render pass.");

        return renderPass;
    }

    public static unsafe Framebuffer[] CreateFramebuffers(GpuState state, RenderPass renderPass)
    {
        var framebuffers = new Framebuffer[state.SwapchainImageViews.Length];

        for (int i = 0; i < state.SwapchainImageViews.Length; i++)
        {
            var attachment = state.SwapchainImageViews[i];
            var fbInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = renderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = state.SwapchainExtent.Width,
                Height = state.SwapchainExtent.Height,
                Layers = 1,
            };

            if (state.Vk.CreateFramebuffer(state.Device, &fbInfo, null, out framebuffers[i]) != Result.Success)
                throw new InvalidOperationException("Failed to create framebuffer.");
        }

        return framebuffers;
    }

    public static unsafe void DestroyFramebuffers(GpuState state, Framebuffer[] framebuffers)
    {
        foreach (var fb in framebuffers)
            state.Vk.DestroyFramebuffer(state.Device, fb, null);
    }

    public static unsafe Pipeline CreateGraphicsPipeline(
        GpuState state, RenderPass renderPass,
        ShaderModule vertModule, ShaderModule fragModule,
        out PipelineLayout pipelineLayout)
    {
        var entryPoint = Marshal.StringToHGlobalAnsi("main");

        try
        {
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertModule,
                PName = (byte*)entryPoint,
            };
            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragModule,
                PName = (byte*)entryPoint,
            };

            // Vertex input
            var bindingDesc = Vertex.BindingDescription;
            var attrDescs = Vertex.AttributeDescriptions;

            fixed (VertexInputAttributeDescription* pAttrs = attrDescs)
            {
                var vertexInputInfo = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1,
                    PVertexBindingDescriptions = &bindingDesc,
                    VertexAttributeDescriptionCount = (uint)attrDescs.Length,
                    PVertexAttributeDescriptions = pAttrs,
                };

                var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList,
                    PrimitiveRestartEnable = false,
                };

                var viewport = new Viewport
                {
                    X = 0, Y = 0,
                    Width = state.SwapchainExtent.Width,
                    Height = state.SwapchainExtent.Height,
                    MinDepth = 0, MaxDepth = 1,
                };

                var scissor = new Rect2D
                {
                    Offset = new Offset2D(0, 0),
                    Extent = state.SwapchainExtent,
                };

                var viewportState = new PipelineViewportStateCreateInfo
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    PViewports = &viewport,
                    ScissorCount = 1,
                    PScissors = &scissor,
                };

                var rasterizer = new PipelineRasterizationStateCreateInfo
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = false,
                    RasterizerDiscardEnable = false,
                    PolygonMode = PolygonMode.Fill,
                    LineWidth = 1.0f,
                    CullMode = CullModeFlags.BackBit,
                    FrontFace = FrontFace.Clockwise,
                    DepthBiasEnable = false,
                };

                var multisampling = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = false,
                    RasterizationSamples = SampleCountFlags.Count1Bit,
                };

                var colorBlendAttachment = new PipelineColorBlendAttachmentState
                {
                    ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                     ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                    BlendEnable = false,
                };

                var colorBlending = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    LogicOpEnable = false,
                    AttachmentCount = 1,
                    PAttachments = &colorBlendAttachment,
                };

                // Dynamic viewport/scissor for resize support
                var dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
                var dynamicState = new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = 2,
                    PDynamicStates = dynamicStates,
                };

                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                };

                if (state.Vk.CreatePipelineLayout(state.Device, &layoutInfo, null, out pipelineLayout) != Result.Success)
                    throw new InvalidOperationException("Failed to create pipeline layout.");

                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = shaderStages,
                    PVertexInputState = &vertexInputInfo,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState = &multisampling,
                    PColorBlendState = &colorBlending,
                    PDynamicState = &dynamicState,
                    Layout = pipelineLayout,
                    RenderPass = renderPass,
                    Subpass = 0,
                };

                if (state.Vk.CreateGraphicsPipelines(state.Device, default, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                    throw new InvalidOperationException("Failed to create graphics pipeline.");

                return pipeline;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(entryPoint);
        }
    }

    public static unsafe RenderPass CreateOffscreenRenderPass(GpuState state, Format format)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = format,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            // Stay in ColorAttachmentOptimal — the render graph barrier
            // handles the transition to ShaderReadOnlyOptimal.
            FinalLayout = ImageLayout.ColorAttachmentOptimal,
        };

        var colorRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal,
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };

        if (state.Vk.CreateRenderPass(state.Device, &renderPassInfo, null, out var renderPass) != Result.Success)
            throw new InvalidOperationException("Failed to create offscreen render pass.");

        return renderPass;
    }

    public static unsafe Framebuffer CreateOffscreenFramebuffer(
        GpuState state, RenderPass renderPass, ImageView imageView, uint width, uint height)
    {
        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
            AttachmentCount = 1,
            PAttachments = &imageView,
            Width = width,
            Height = height,
            Layers = 1,
        };

        if (state.Vk.CreateFramebuffer(state.Device, &fbInfo, null, out var fb) != Result.Success)
            throw new InvalidOperationException("Failed to create offscreen framebuffer.");

        return fb;
    }

    public static unsafe Pipeline CreatePostProcessPipeline(
        GpuState state, RenderPass renderPass,
        DescriptorSetLayout descriptorSetLayout,
        ShaderModule vertModule, ShaderModule fragModule,
        out PipelineLayout pipelineLayout)
    {
        var entryPoint = Marshal.StringToHGlobalAnsi("main");

        try
        {
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertModule,
                PName = (byte*)entryPoint,
            };
            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragModule,
                PName = (byte*)entryPoint,
            };

            // No vertex input — fullscreen triangle generated in shader
            var vertexInputInfo = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };

            var viewport = new Viewport(0, 0,
                state.SwapchainExtent.Width, state.SwapchainExtent.Height, 0, 1);
            var scissor = new Rect2D(new Offset2D(0, 0), state.SwapchainExtent);

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor,
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = false,
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment,
            };

            var dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates,
            };

            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &descriptorSetLayout,
            };

            if (state.Vk.CreatePipelineLayout(state.Device, &layoutInfo, null, out pipelineLayout) != Result.Success)
                throw new InvalidOperationException("Failed to create post-process pipeline layout.");

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInputInfo,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisampling,
                PColorBlendState = &colorBlending,
                PDynamicState = &dynamicState,
                Layout = pipelineLayout,
                RenderPass = renderPass,
                Subpass = 0,
            };

            if (state.Vk.CreateGraphicsPipelines(state.Device, default, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                throw new InvalidOperationException("Failed to create post-process pipeline.");

            return pipeline;
        }
        finally
        {
            Marshal.FreeHGlobal(entryPoint);
        }
    }

    // ─── M3: GBuffer render pass (3 color MRT + depth) ─────────────────

    public static unsafe RenderPass CreateGBufferRenderPass(GpuState state) =>
        CreateGBufferRenderPass(state, state.Capabilities.DepthFormat);

    public static unsafe RenderPass CreateGBufferRenderPass(GpuState state, Format depthFormat)
    {
        var attachments = stackalloc AttachmentDescription[4];

        // 0: Position (RGBA16F)
        attachments[0] = new AttachmentDescription
        {
            Format = GBufferPositionFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.ColorAttachmentOptimal,
        };
        // 1: Normal (RGBA16F)
        attachments[1] = attachments[0];
        attachments[1].Format = GBufferNormalFormat;
        // 2: Albedo (RGBA8)
        attachments[2] = attachments[0];
        attachments[2].Format = GBufferAlbedoFormat;
        // 3: Depth (Store so it can be sampled for debug visualization / SSAO)
        attachments[3] = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };

        var colorRefs = stackalloc AttachmentReference[3];
        colorRefs[0] = new AttachmentReference { Attachment = 0, Layout = ImageLayout.ColorAttachmentOptimal };
        colorRefs[1] = new AttachmentReference { Attachment = 1, Layout = ImageLayout.ColorAttachmentOptimal };
        colorRefs[2] = new AttachmentReference { Attachment = 2, Layout = ImageLayout.ColorAttachmentOptimal };

        var depthRef = new AttachmentReference
        {
            Attachment = 3,
            Layout = ImageLayout.DepthStencilAttachmentOptimal,
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 3,
            PColorAttachments = colorRefs,
            PDepthStencilAttachment = &depthRef,
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit,
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 4,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };

        if (state.Vk.CreateRenderPass(state.Device, &renderPassInfo, null, out var renderPass) != Result.Success)
            throw new InvalidOperationException("Failed to create GBuffer render pass.");

        return renderPass;
    }

    public static unsafe Framebuffer CreateGBufferFramebuffer(
        GpuState state, RenderPass renderPass,
        ImageView posView, ImageView normalView, ImageView albedoView, ImageView depthView,
        uint width, uint height)
    {
        var attachments = stackalloc ImageView[4];
        attachments[0] = posView;
        attachments[1] = normalView;
        attachments[2] = albedoView;
        attachments[3] = depthView;

        var fbInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
            AttachmentCount = 4,
            PAttachments = attachments,
            Width = width,
            Height = height,
            Layers = 1,
        };

        if (state.Vk.CreateFramebuffer(state.Device, &fbInfo, null, out var fb) != Result.Success)
            throw new InvalidOperationException("Failed to create GBuffer framebuffer.");

        return fb;
    }

    // ─── M3: GBuffer graphics pipeline (Vertex3D input, push constants, MRT, depth test) ──

    public static unsafe Pipeline CreateGBufferPipeline(
        GpuState state, RenderPass renderPass,
        ShaderModule vertModule, ShaderModule fragModule,
        VertexInputBindingDescription bindingDesc,
        VertexInputAttributeDescription[] attrDescs,
        uint pushConstantSize,
        out PipelineLayout pipelineLayout)
    {
        var entryPoint = Marshal.StringToHGlobalAnsi("main");

        try
        {
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertModule,
                PName = (byte*)entryPoint,
            };
            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragModule,
                PName = (byte*)entryPoint,
            };

            fixed (VertexInputAttributeDescription* pAttrs = attrDescs)
            {
                var vertexInputInfo = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1,
                    PVertexBindingDescriptions = &bindingDesc,
                    VertexAttributeDescriptionCount = (uint)attrDescs.Length,
                    PVertexAttributeDescriptions = pAttrs,
                };

                var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList,
                };

                var viewport = new Viewport(0, 0,
                    state.SwapchainExtent.Width, state.SwapchainExtent.Height, 0, 1);
                var scissor = new Rect2D(new Offset2D(0, 0), state.SwapchainExtent);

                var viewportState = new PipelineViewportStateCreateInfo
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    PViewports = &viewport,
                    ScissorCount = 1,
                    PScissors = &scissor,
                };

                var rasterizer = new PipelineRasterizationStateCreateInfo
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    PolygonMode = PolygonMode.Fill,
                    LineWidth = 1.0f,
                    CullMode = CullModeFlags.BackBit,
                    FrontFace = FrontFace.CounterClockwise,
                };

                var multisampling = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    RasterizationSamples = SampleCountFlags.Count1Bit,
                };

                var depthStencil = new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = true,
                    DepthWriteEnable = true,
                    DepthCompareOp = CompareOp.Less,
                    DepthBoundsTestEnable = false,
                    StencilTestEnable = false,
                };

                // 3 color blend attachments (one per MRT target), no blending
                var colorBlendAttachments = stackalloc PipelineColorBlendAttachmentState[3];
                for (int i = 0; i < 3; i++)
                {
                    colorBlendAttachments[i] = new PipelineColorBlendAttachmentState
                    {
                        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                         ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                        BlendEnable = false,
                    };
                }

                var colorBlending = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    AttachmentCount = 3,
                    PAttachments = colorBlendAttachments,
                };

                var dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
                var dynamicState = new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = 2,
                    PDynamicStates = dynamicStates,
                };

                var pushConstantRange = new PushConstantRange
                {
                    StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                    Offset = 0,
                    Size = pushConstantSize,
                };

                var layoutInfo = new PipelineLayoutCreateInfo
                {
                    SType = StructureType.PipelineLayoutCreateInfo,
                    PushConstantRangeCount = 1,
                    PPushConstantRanges = &pushConstantRange,
                };

                if (state.Vk.CreatePipelineLayout(state.Device, &layoutInfo, null, out pipelineLayout) != Result.Success)
                    throw new InvalidOperationException("Failed to create GBuffer pipeline layout.");

                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = shaderStages,
                    PVertexInputState = &vertexInputInfo,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState = &multisampling,
                    PDepthStencilState = &depthStencil,
                    PColorBlendState = &colorBlending,
                    PDynamicState = &dynamicState,
                    Layout = pipelineLayout,
                    RenderPass = renderPass,
                    Subpass = 0,
                };

                if (state.Vk.CreateGraphicsPipelines(state.Device, default, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                    throw new InvalidOperationException("Failed to create GBuffer pipeline.");

                return pipeline;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(entryPoint);
        }
    }

    // ─── M3: Fullscreen-quad pipeline with descriptor set + push constants ──

    public static unsafe Pipeline CreateFullscreenPipeline(
        GpuState state, RenderPass renderPass,
        DescriptorSetLayout descriptorSetLayout,
        ShaderModule vertModule, ShaderModule fragModule,
        uint pushConstantSize, ShaderStageFlags pushConstantStage,
        out PipelineLayout pipelineLayout)
    {
        var entryPoint = Marshal.StringToHGlobalAnsi("main");

        try
        {
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertModule,
                PName = (byte*)entryPoint,
            };
            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragModule,
                PName = (byte*)entryPoint,
            };

            var vertexInputInfo = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };

            var viewport = new Viewport(0, 0,
                state.SwapchainExtent.Width, state.SwapchainExtent.Height, 0, 1);
            var scissor = new Rect2D(new Offset2D(0, 0), state.SwapchainExtent);

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor,
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = false,
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment,
            };

            var dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
            var dynamicState = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 2,
                PDynamicStates = dynamicStates,
            };

            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &descriptorSetLayout,
            };

            if (pushConstantSize > 0)
            {
                var pushConstantRange = new PushConstantRange
                {
                    StageFlags = pushConstantStage,
                    Offset = 0,
                    Size = pushConstantSize,
                };
                layoutInfo.PushConstantRangeCount = 1;
                layoutInfo.PPushConstantRanges = &pushConstantRange;
            }

            if (state.Vk.CreatePipelineLayout(state.Device, &layoutInfo, null, out pipelineLayout) != Result.Success)
                throw new InvalidOperationException("Failed to create fullscreen pipeline layout.");

            var pipelineInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = shaderStages,
                PVertexInputState = &vertexInputInfo,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisampling,
                PColorBlendState = &colorBlending,
                PDynamicState = &dynamicState,
                Layout = pipelineLayout,
                RenderPass = renderPass,
                Subpass = 0,
            };

            if (state.Vk.CreateGraphicsPipelines(state.Device, default, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                throw new InvalidOperationException("Failed to create fullscreen pipeline.");

            return pipeline;
        }
        finally
        {
            Marshal.FreeHGlobal(entryPoint);
        }
    }

    // ─── M3: Swapchain render pass with LoadOp.Load (for ImGui overlay) ──

    public static unsafe RenderPass CreateOverlayRenderPass(GpuState state)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = state.SwapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.PresentSrcKhr,
            FinalLayout = ImageLayout.PresentSrcKhr,
        };

        var colorRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal,
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit,
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };

        if (state.Vk.CreateRenderPass(state.Device, &renderPassInfo, null, out var renderPass) != Result.Success)
            throw new InvalidOperationException("Failed to create overlay render pass.");

        return renderPass;
    }
}
