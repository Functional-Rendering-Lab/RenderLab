using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using RenderLab.Gpu;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RenderLab.Ui.ImGui;

using ImGui = ImGuiNET.ImGui;

public sealed class VulkanImGui : IDisposable
{
    private readonly GpuState _state;

    // Font atlas
    private Image _fontImage;
    private Allocation _fontAlloc;
    private ImageView _fontView;
    private Sampler _fontSampler;

    // Pipeline
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _fontDescriptorSet;
    private PipelineLayout _pipelineLayout;
    private Pipeline _pipeline;

    // Per-frame vertex/index buffers. Capacity grows in doubling steps so
    // vkAllocateMemory is hit O(log N) at warm-up instead of every frame.
    // Buffers stay mapped for the lifetime of this instance.
    private readonly Buffer[] _vertexBuffers;
    private readonly Allocation[] _vertexAllocs;
    private readonly ulong[] _vertexCapacities;
    private readonly IntPtr[] _vertexMapped;
    private readonly Buffer[] _indexBuffers;
    private readonly Allocation[] _indexAllocs;
    private readonly ulong[] _indexCapacities;
    private readonly IntPtr[] _indexMapped;

    private VulkanImGui(GpuState state)
    {
        _state = state;
        int frames = GpuState.MaxFramesInFlight;
        _vertexBuffers = new Buffer[frames];
        _vertexAllocs = new Allocation[frames];
        _vertexCapacities = new ulong[frames];
        _vertexMapped = new IntPtr[frames];
        _indexBuffers = new Buffer[frames];
        _indexAllocs = new Allocation[frames];
        _indexCapacities = new ulong[frames];
        _indexMapped = new IntPtr[frames];
    }

    private static string ResolveIniPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".renderlab");
        string dir = Path.Combine(root, "RenderLab");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "imgui.ini");
    }

    public static unsafe VulkanImGui Create(GpuState state, RenderPass renderPass)
    {
        var imgui = new VulkanImGui(state);
        var ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags  |= ImGuiConfigFlags.DockingEnable;
        io.NativePtr->IniFilename = (byte*)Marshal.StringToCoTaskMemUTF8(ResolveIniPath());

        ImGuiTheme.Apply();
        ImGuiTheme.LoadFont(io);
        imgui.CreateFontAtlas();
        imgui.CreateDescriptorResources();
        imgui.CreatePipeline(renderPass);

        return imgui;
    }

    public void NewFrame(int width, int height, float deltaTime)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(width, height);
        io.DeltaTime = deltaTime > 0 ? deltaTime : 1f / 60f;
        ImGui.NewFrame();
    }

    public unsafe void RecordCommands(Vk vk, CommandBuffer cmd, RenderPass renderPass,
        Framebuffer framebuffer, Extent2D extent)
    {
        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        if (drawData.CmdListsCount == 0) return;

        var renderPassBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = renderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D(new Offset2D(0, 0), extent),
        };

        vk.CmdBeginRenderPass(cmd, &renderPassBegin, SubpassContents.Inline);
        vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _pipeline);

        // Set viewport
        var viewport = new Viewport(0, 0, drawData.DisplaySize.X, drawData.DisplaySize.Y, 0, 1);
        vk.CmdSetViewport(cmd, 0, 1, &viewport);

        // Upload vertex/index buffers
        int frame = _state.CurrentFrame;
        UploadBuffers(drawData, frame);

        if (_vertexCapacities[frame] > 0)
        {
            var vb = _vertexBuffers[frame];
            ulong offset = 0;
            vk.CmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);
            vk.CmdBindIndexBuffer(cmd, _indexBuffers[frame], 0,
                sizeof(ushort) == 2 ? IndexType.Uint16 : IndexType.Uint32);
        }

        // Push constants: orthographic projection
        var L = drawData.DisplayPos.X;
        var R = drawData.DisplayPos.X + drawData.DisplaySize.X;
        var T = drawData.DisplayPos.Y;
        var B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;
        var proj = stackalloc float[16]
        {
            2f/(R-L),     0,            0, 0,
            0,            2f/(B-T),     0, 0,
            0,            0,           -1, 0,
            (R+L)/(L-R),  (T+B)/(T-B), 0, 1
        };
        vk.CmdPushConstants(cmd, _pipelineLayout, ShaderStageFlags.VertexBit, 0, 64, proj);

        // Bind font descriptor
        var ds = _fontDescriptorSet;
        vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Graphics, _pipelineLayout, 0, 1, &ds, 0, null);

        // Draw
        var clipOff = drawData.DisplayPos;
        int vtxOffset = 0;
        int idxOffset = 0;

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                var pcmd = cmdList.CmdBuffer[i];

                var clipMin = new Vector2(pcmd.ClipRect.X - clipOff.X, pcmd.ClipRect.Y - clipOff.Y);
                var clipMax = new Vector2(pcmd.ClipRect.Z - clipOff.X, pcmd.ClipRect.W - clipOff.Y);
                if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y) continue;

                var scissor = new Rect2D(
                    new Offset2D((int)clipMin.X, (int)clipMin.Y),
                    new Extent2D((uint)(clipMax.X - clipMin.X), (uint)(clipMax.Y - clipMin.Y)));
                vk.CmdSetScissor(cmd, 0, 1, &scissor);

                vk.CmdDrawIndexed(cmd, pcmd.ElemCount, 1,
                    pcmd.IdxOffset + (uint)idxOffset,
                    (int)pcmd.VtxOffset + vtxOffset, 0);
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }

        vk.CmdEndRenderPass(cmd);
    }

    // Grow per-frame buffers in doubling steps and keep them mapped for the
    // lifetime of the instance. On warm-up vkAllocateMemory fires O(log N)
    // times; steady state hits zero.
    private unsafe void UploadBuffers(ImDrawDataPtr drawData, int frame)
    {
        ulong vtxSize = (ulong)(drawData.TotalVtxCount * sizeof(ImDrawVert));
        ulong idxSize = (ulong)(drawData.TotalIdxCount * sizeof(ushort));
        if (vtxSize == 0 || idxSize == 0) return;

        EnsureCapacity(ref _vertexBuffers[frame], ref _vertexAllocs[frame],
            ref _vertexCapacities[frame], ref _vertexMapped[frame],
            BufferUsageFlags.VertexBufferBit, vtxSize);
        EnsureCapacity(ref _indexBuffers[frame], ref _indexAllocs[frame],
            ref _indexCapacities[frame], ref _indexMapped[frame],
            BufferUsageFlags.IndexBufferBit, idxSize);

        byte* vtxDst = (byte*)_vertexMapped[frame];
        byte* idxDst = (byte*)_indexMapped[frame];

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            ulong vtxBytes = (ulong)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert));
            System.Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, vtxDst, (long)vtxBytes, (long)vtxBytes);
            vtxDst += vtxBytes;

            ulong idxBytes = (ulong)(cmdList.IdxBuffer.Size * sizeof(ushort));
            System.Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, idxDst, (long)idxBytes, (long)idxBytes);
            idxDst += idxBytes;
        }
    }

    private unsafe void EnsureCapacity(
        ref Buffer buffer, ref Allocation alloc, ref ulong capacity, ref IntPtr mapped,
        BufferUsageFlags usage, ulong needed)
    {
        if (needed <= capacity) return;

        if (capacity > 0)
        {
            _state.Allocator.Unmap(_state, alloc);
            _state.Allocator.DestroyBuffer(_state, buffer, alloc);
        }

        ulong newCap = NextPow2(Math.Max(needed, capacity + 1));
        (buffer, alloc) = _state.Allocator.AllocateBuffer(_state, newCap, usage, MemoryIntent.CpuToGpu);
        mapped = (IntPtr)_state.Allocator.Map(_state, alloc);
        capacity = newCap;
    }

    private static ulong NextPow2(ulong v)
    {
        if (v <= 1) return 1;
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v |= v >> 32;
        return v + 1;
    }

    private unsafe void CreateFontAtlas()
    {
        var vk = _state.Vk;
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out nint pixels, out int width, out int height, out int bytesPerPixel);
        ulong uploadSize = (ulong)(width * height * bytesPerPixel);

        // Create font image via the central allocator
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        (_fontImage, _fontAlloc) = _state.Allocator.AllocateImage(_state, in imageInfo, MemoryIntent.GpuOnly);

        // Staging buffer
        var (stagingBuffer, stagingAlloc) = _state.Allocator.AllocateBuffer(
            _state, uploadSize, BufferUsageFlags.TransferSrcBit, MemoryIntent.CpuToGpu);

        void* mapped = _state.Allocator.Map(_state, stagingAlloc);
        System.Buffer.MemoryCopy((void*)pixels, mapped, (long)uploadSize, (long)uploadSize);
        _state.Allocator.Unmap(_state, stagingAlloc);

        // Upload via one-shot command buffer
        var cmdAllocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _state.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        CommandBuffer cmd;
        vk.AllocateCommandBuffers(_state.Device, &cmdAllocInfo, &cmd);

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        vk.BeginCommandBuffer(cmd, &beginInfo);

        // Transition to transfer dst
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = _fontImage,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LevelCount = 1, LayerCount = 1,
            },
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.TransferWriteBit,
        };
        vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit,
            0, 0, null, 0, null, 1, &barrier);

        // Copy
        var region = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LayerCount = 1,
            },
            ImageExtent = new Extent3D((uint)width, (uint)height, 1),
        };
        vk.CmdCopyBufferToImage(cmd, stagingBuffer, _fontImage, ImageLayout.TransferDstOptimal, 1, &region);

        // Transition to shader read
        barrier.OldLayout = ImageLayout.TransferDstOptimal;
        barrier.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
        barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
        barrier.DstAccessMask = AccessFlags.ShaderReadBit;
        vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit,
            0, 0, null, 0, null, 1, &barrier);

        vk.EndCommandBuffer(cmd);

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
        };
        vk.QueueSubmit(_state.GraphicsQueue, 1, &submitInfo, default);
        vk.QueueWaitIdle(_state.GraphicsQueue);

        vk.FreeCommandBuffers(_state.Device, _state.CommandPool, 1, &cmd);
        _state.Allocator.DestroyBuffer(_state, stagingBuffer, stagingAlloc);

        // Image view
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _fontImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LevelCount = 1, LayerCount = 1,
            },
        };
        if (vk.CreateImageView(_state.Device, &viewInfo, null, out _fontView) != Result.Success)
            throw new InvalidOperationException("Failed to create ImGui font image view.");

        // Sampler
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
        };
        if (vk.CreateSampler(_state.Device, &samplerInfo, null, out _fontSampler) != Result.Success)
            throw new InvalidOperationException("Failed to create ImGui font sampler.");

        io.Fonts.SetTexID(0);
    }

    private unsafe void CreateDescriptorResources()
    {
        var vk = _state.Vk;

        // Layout
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
        if (vk.CreateDescriptorSetLayout(_state.Device, &layoutInfo, null, out _descriptorSetLayout) != Result.Success)
            throw new InvalidOperationException("Failed to create ImGui descriptor set layout.");

        // Pool
        var poolSize = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
        };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
        };
        if (vk.CreateDescriptorPool(_state.Device, &poolInfo, null, out _descriptorPool) != Result.Success)
            throw new InvalidOperationException("Failed to create ImGui descriptor pool.");

        // Allocate set
        var setLayout = _descriptorSetLayout;
        var setAllocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        DescriptorSet set;
        if (vk.AllocateDescriptorSets(_state.Device, &setAllocInfo, &set) != Result.Success)
            throw new InvalidOperationException("Failed to allocate ImGui descriptor set.");
        _fontDescriptorSet = set;

        // Write
        var imageInfoDesc = new DescriptorImageInfo
        {
            Sampler = _fontSampler,
            ImageView = _fontView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _fontDescriptorSet,
            DstBinding = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &imageInfoDesc,
        };
        vk.UpdateDescriptorSets(_state.Device, 1, &write, 0, null);
    }

    private unsafe void CreatePipeline(RenderPass renderPass)
    {
        var vk = _state.Vk;

        // Inline SPIR-V for ImGui shaders — minimal vertex+fragment pair
        var vertSpv = CompileImGuiVertexShader();
        var fragSpv = CompileImGuiFragmentShader();

        var vertModule = CreateShaderModule(vertSpv);
        var fragModule = CreateShaderModule(fragSpv);

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

            // ImDrawVert: pos(vec2) + uv(vec2) + col(u32)
            var bindingDesc = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = (uint)sizeof(ImDrawVert),
                InputRate = VertexInputRate.Vertex,
            };

            var attrDescs = stackalloc VertexInputAttributeDescription[3];
            attrDescs[0] = new VertexInputAttributeDescription
            {
                Location = 0, Binding = 0,
                Format = Format.R32G32Sfloat,
                Offset = 0, // pos
            };
            attrDescs[1] = new VertexInputAttributeDescription
            {
                Location = 1, Binding = 0,
                Format = Format.R32G32Sfloat,
                Offset = 8, // uv
            };
            attrDescs[2] = new VertexInputAttributeDescription
            {
                Location = 2, Binding = 0,
                Format = Format.R8G8B8A8Unorm,
                Offset = 16, // col
            };

            var vertexInputInfo = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 1,
                PVertexBindingDescriptions = &bindingDesc,
                VertexAttributeDescriptionCount = 3,
                PVertexAttributeDescriptions = attrDescs,
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };

            var viewport = new Viewport(0, 0, 1, 1, 0, 1);
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(1, 1));
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

            // Alpha blending
            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = true,
                SrcColorBlendFactor = BlendFactor.SrcAlpha,
                DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = BlendOp.Add,
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

            var pushConstantRange = new PushConstantRange
            {
                StageFlags = ShaderStageFlags.VertexBit,
                Offset = 0,
                Size = 64, // mat4 projection
            };

            var dsLayout = _descriptorSetLayout;
            var layoutCreateInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &dsLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushConstantRange,
            };

            if (vk.CreatePipelineLayout(_state.Device, &layoutCreateInfo, null, out _pipelineLayout) != Result.Success)
                throw new InvalidOperationException("Failed to create ImGui pipeline layout.");

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
                Layout = _pipelineLayout,
                RenderPass = renderPass,
                Subpass = 0,
            };

            if (vk.CreateGraphicsPipelines(_state.Device, default, 1, &pipelineInfo, null, out _pipeline) != Result.Success)
                throw new InvalidOperationException("Failed to create ImGui pipeline.");
        }
        finally
        {
            Marshal.FreeHGlobal(entryPoint);
            vk.DestroyShaderModule(_state.Device, vertModule, null);
            vk.DestroyShaderModule(_state.Device, fragModule, null);
        }
    }

    private unsafe ShaderModule CreateShaderModule(byte[] spirv)
    {
        fixed (byte* code = spirv)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)code,
            };
            if (_state.Vk.CreateShaderModule(_state.Device, &createInfo, null, out var module) != Result.Success)
                throw new InvalidOperationException("Failed to create ImGui shader module.");
            return module;
        }
    }

    // Load ImGui shaders from SPIR-V files compiled alongside other shaders
    private static byte[] CompileImGuiVertexShader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "shaders", "imgui.vert.spv");
        if (File.Exists(path)) return File.ReadAllBytes(path);
        throw new FileNotFoundException("ImGui vertex shader not found. Run compile_shaders.py first.", path);
    }

    private static byte[] CompileImGuiFragmentShader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "shaders", "imgui.frag.spv");
        if (File.Exists(path)) return File.ReadAllBytes(path);
        throw new FileNotFoundException("ImGui fragment shader not found. Run compile_shaders.py first.", path);
    }

    public unsafe void Dispose()
    {
        var vk = _state.Vk;

        for (int i = 0; i < GpuState.MaxFramesInFlight; i++)
        {
            if (_vertexCapacities[i] > 0)
            {
                _state.Allocator.Unmap(_state, _vertexAllocs[i]);
                _state.Allocator.DestroyBuffer(_state, _vertexBuffers[i], _vertexAllocs[i]);
            }
            if (_indexCapacities[i] > 0)
            {
                _state.Allocator.Unmap(_state, _indexAllocs[i]);
                _state.Allocator.DestroyBuffer(_state, _indexBuffers[i], _indexAllocs[i]);
            }
        }

        vk.DestroyPipeline(_state.Device, _pipeline, null);
        vk.DestroyPipelineLayout(_state.Device, _pipelineLayout, null);
        vk.DestroyDescriptorPool(_state.Device, _descriptorPool, null);
        vk.DestroyDescriptorSetLayout(_state.Device, _descriptorSetLayout, null);
        vk.DestroySampler(_state.Device, _fontSampler, null);
        vk.DestroyImageView(_state.Device, _fontView, null);
        _state.Allocator.DestroyImage(_state, _fontImage, _fontAlloc);

        ImGui.DestroyContext();
    }
}
