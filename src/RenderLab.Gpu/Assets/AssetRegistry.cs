using System.Collections.Immutable;
using RenderLab.Assets;
using Silk.NET.Vulkan;
using FResult = RenderLab.Functional.Result;
using FMeshId = RenderLab.Functional.Result<RenderLab.Assets.MeshId, RenderLab.Assets.AssetError>;
using FTextureId = RenderLab.Functional.Result<RenderLab.Assets.TextureId, RenderLab.Assets.AssetError>;
using FMaterialId = RenderLab.Functional.Result<RenderLab.Assets.MaterialId, RenderLab.Assets.AssetError>;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace RenderLab.Gpu.Assets;

/// <summary>
/// The single owner of mesh and texture assets and their backing GPU resources.
/// Implements the pure <see cref="IAssetCatalog"/> view consumed by scene/UI
/// code, and the shell-side <see cref="IGpuAssetResolver"/> used by recording
/// passes. Counters start at 1 so 0 is reserved for the <c>None</c> sentinels.
/// </summary>
public sealed class AssetRegistry : IAssetCatalog, IGpuAssetResolver, IDisposable
{
    private readonly GpuState _gpu;
    private readonly Dictionary<int, MeshAsset> _meshes = new();
    private readonly Dictionary<int, MeshGpu> _meshGpu = new();
    private readonly Dictionary<int, TextureAsset> _textures = new();
    private readonly Dictionary<int, TextureGpu> _textureGpu = new();
    private readonly Dictionary<int, MaterialAsset> _materials = new();
    private int _nextMeshId = 1;
    private int _nextTextureId = 1;
    private int _nextMaterialId = 1;

    private Sampler _sharedSampler;
    private TextureId _whiteFallbackId;
    private MaterialId _defaultMaterialId;

    private readonly record struct MeshGpu(VkBuffer VertexBuffer, Allocation VertexAlloc, VkBuffer IndexBuffer, Allocation IndexAlloc, uint IndexCount);
    private readonly record struct TextureGpu(Image Image, Allocation Alloc, ImageView View);

    public AssetRegistry(GpuState gpu)
    {
        _gpu = gpu;
        _sharedSampler = VulkanImage.CreateSampler(gpu);
        _whiteFallbackId = RegisterBuiltinWhite();
        _defaultMaterialId = RegisterBuiltinDefaultMaterial();
    }

    // ─── Mesh ──────────────────────────────────────────────────────────

    public FMeshId RegisterMesh(string name, MeshData data)
    {
        try
        {
            var (vb, va) = VulkanBuffer.Create<Vertex3D>(_gpu, BufferUsageFlags.VertexBufferBit, data.Vertices.AsSpan());
            var (ib, ia) = VulkanBuffer.Create<uint>(_gpu, BufferUsageFlags.IndexBufferBit, data.Indices.AsSpan());

            var id = new MeshId(_nextMeshId++);
            _meshes[id.Value] = new MeshAsset(id, name, data);
            _meshGpu[id.Value] = new MeshGpu(vb, va, ib, ia, (uint)data.Indices.Length);
            return FResult.Ok<MeshId, AssetError>(id);
        }
        catch (Exception ex)
        {
            return FResult.Error<MeshId, AssetError>(new AssetError.GpuUploadFailed(ex.Message));
        }
    }

    public FMeshId LoadMesh(string path)
    {
        MeshData data;
        try
        {
            data = ObjLoader.Load(path);
        }
        catch (FileNotFoundException)
        {
            return FResult.Error<MeshId, AssetError>(new AssetError.FileNotFound(path));
        }
        catch (Exception ex)
        {
            return FResult.Error<MeshId, AssetError>(new AssetError.InvalidFormat(path, ex.Message));
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return RegisterMesh(name, data);
    }

    public MeshAsset GetMesh(MeshId id) =>
        _meshes.TryGetValue(id.Value, out var asset)
            ? asset
            : throw new KeyNotFoundException($"Unknown MeshId({id.Value})");

    public bool TryGetMesh(MeshId id, out MeshAsset asset) =>
        _meshes.TryGetValue(id.Value, out asset!);

    public IEnumerable<MeshAsset> AllMeshes => _meshes.Values;

    public GpuMeshHandles ResolveMesh(MeshId id)
    {
        if (!_meshGpu.TryGetValue(id.Value, out var gpu))
            throw new KeyNotFoundException($"Unknown MeshId({id.Value})");
        return new GpuMeshHandles(gpu.VertexBuffer, gpu.IndexBuffer, gpu.IndexCount);
    }

    // ─── Texture ───────────────────────────────────────────────────────

    public FTextureId RegisterTexture(string name, int width, int height, TextureFormat format, ReadOnlySpan<byte> pixels)
    {
        var expected = width * height * 4;
        if (pixels.Length != expected)
            return FResult.Error<TextureId, AssetError>(
                new AssetError.InvalidFormat(name, $"expected {expected} bytes for {width}x{height} RGBA8, got {pixels.Length}"));

        try
        {
            var (image, alloc, view) = UploadTexture(width, height, format, pixels);

            var id = new TextureId(_nextTextureId++);
            _textures[id.Value] = new TextureAsset(id, name, width, height, format, pixels.ToArray());
            _textureGpu[id.Value] = new TextureGpu(image, alloc, view);
            return FResult.Ok<TextureId, AssetError>(id);
        }
        catch (Exception ex)
        {
            return FResult.Error<TextureId, AssetError>(new AssetError.GpuUploadFailed(ex.Message));
        }
    }

    public FTextureId RegisterTexture(string name, TextureAsset asset) =>
        RegisterTexture(name, asset.Width, asset.Height, asset.Format, asset.Pixels);

    public TextureAsset GetTexture(TextureId id) =>
        _textures.TryGetValue(id.Value, out var asset)
            ? asset
            : throw new KeyNotFoundException($"Unknown TextureId({id.Value})");

    public bool TryGetTexture(TextureId id, out TextureAsset asset) =>
        _textures.TryGetValue(id.Value, out asset!);

    public IEnumerable<TextureAsset> AllTextures => _textures.Values;

    public GpuTextureHandles ResolveTexture(TextureId id)
    {
        var key = id.IsNone ? _whiteFallbackId.Value : id.Value;
        if (!_textureGpu.TryGetValue(key, out var gpu))
            throw new KeyNotFoundException($"Unknown TextureId({id.Value})");
        return new GpuTextureHandles(gpu.View, _sharedSampler);
    }

    // ─── Material ──────────────────────────────────────────────────────

    public FMaterialId RegisterMaterial(string name, Func<MaterialId, MaterialAsset> build)
    {
        var id = new MaterialId(_nextMaterialId++);
        _materials[id.Value] = build(id);
        return FResult.Ok<MaterialId, AssetError>(id);
    }

    /// <summary>
    /// In-place edit of an existing material asset. The editor calls this when
    /// the user moves a slider; consumers re-resolve via <see cref="GetMaterial"/>
    /// each frame, so the new value lights up immediately.
    /// </summary>
    public void UpdateMaterial(MaterialId id, MaterialAsset asset)
    {
        if (!_materials.ContainsKey(id.Value))
            throw new KeyNotFoundException($"Unknown MaterialId({id.Value})");
        if (asset.Id != id)
            throw new ArgumentException($"Asset id {asset.Id} does not match update target {id}.", nameof(asset));
        _materials[id.Value] = asset;
    }

    public MaterialAsset GetMaterial(MaterialId id)
    {
        var key = id.IsNone ? _defaultMaterialId.Value : id.Value;
        return _materials.TryGetValue(key, out var asset)
            ? asset
            : throw new KeyNotFoundException($"Unknown MaterialId({id.Value})");
    }

    public bool TryGetMaterial(MaterialId id, out MaterialAsset asset) =>
        _materials.TryGetValue(id.IsNone ? _defaultMaterialId.Value : id.Value, out asset!);

    public IEnumerable<MaterialAsset> AllMaterials => _materials.Values;

    // ─── glTF import ───────────────────────────────────────────────────

    /// <summary>
    /// Loads a glTF / glb file and registers every asset it contains in
    /// dependency order: textures first, then materials (with rewritten
    /// texture refs), then meshes, then drawable seeds. The pure parsing
    /// step lives in <see cref="GltfLoader"/>; this method is the impure
    /// orchestration boundary.
    /// </summary>
    public RenderLab.Functional.Result<GltfImportResult, AssetError> ImportGltf(string path)
    {
        GltfImport import;
        try
        {
            import = GltfLoader.Load(path);
        }
        catch (FileNotFoundException)
        {
            return FResult.Error<GltfImportResult, AssetError>(new AssetError.FileNotFound(path));
        }
        catch (Exception ex)
        {
            return FResult.Error<GltfImportResult, AssetError>(new AssetError.InvalidFormat(path, ex.Message));
        }

        AssetError? failure = null;

        var textureIds = ImmutableArray.CreateBuilder<TextureId>(import.Textures.Length);
        foreach (var t in import.Textures)
        {
            RegisterTexture(t.Name, t.Width, t.Height, t.Format, t.Pixels).Match(
                ok: id => { textureIds.Add(id); return 0; },
                error: e => { failure = e; return 0; });
            if (failure is not null) return FResult.Error<GltfImportResult, AssetError>(failure);
        }

        var materialIds = ImmutableArray.CreateBuilder<MaterialId>(import.Materials.Length);
        foreach (var m in import.Materials)
        {
            var albedoMap = m.AlbedoTextureIndex >= 0 && m.AlbedoTextureIndex < textureIds.Count
                ? textureIds[m.AlbedoTextureIndex]
                : TextureId.None;
            RegisterMaterial(m.Name, id => new BlinnPhongMaterial(
                id, m.Name, m.Albedo, m.SpecularStrength, m.Shininess, albedoMap)).Match(
                ok: id => { materialIds.Add(id); return 0; },
                error: e => { failure = e; return 0; });
            if (failure is not null) return FResult.Error<GltfImportResult, AssetError>(failure);
        }

        var meshIds = ImmutableArray.CreateBuilder<MeshId>(import.Meshes.Length);
        foreach (var msh in import.Meshes)
        {
            RegisterMesh(msh.Name, msh.Data).Match(
                ok: id => { meshIds.Add(id); return 0; },
                error: e => { failure = e; return 0; });
            if (failure is not null) return FResult.Error<GltfImportResult, AssetError>(failure);
        }

        var drawables = ImmutableArray.CreateBuilder<ImportedDrawable>(import.Drawables.Length);
        foreach (var d in import.Drawables)
        {
            // Drop drawables that reference an unparseable mesh; tolerate
            // missing materials by falling back to the registry default.
            if (d.MeshIndex < 0 || d.MeshIndex >= meshIds.Count) continue;
            var matId = d.MaterialIndex >= 0 && d.MaterialIndex < materialIds.Count
                ? materialIds[d.MaterialIndex]
                : MaterialId.None;
            drawables.Add(new ImportedDrawable(
                d.Name, meshIds[d.MeshIndex], matId, d.Position, d.Scale));
        }

        return FResult.Ok<GltfImportResult, AssetError>(new GltfImportResult(
            meshIds.ToImmutable(), textureIds.ToImmutable(),
            materialIds.ToImmutable(), drawables.ToImmutable()));
    }

    // ─── Built-in fallback ────────────────────────────────────────────

    private MaterialId RegisterBuiltinDefaultMaterial()
    {
        // Untextured grey Blinn-Phong; resolves whenever a Drawable carries
        // MaterialId.None, so render code can dereference unconditionally.
        var result = RegisterMaterial("__default", id => BlinnPhongMaterial.Default(id, "__default"));
        return result.Match(
            ok: id => id,
            error: e => throw new InvalidOperationException($"Failed to register default material: {e.Message}"));
    }

    private TextureId RegisterBuiltinWhite()
    {
        // Single opaque white texel sampled whenever a drawable has no albedo map.
        // sRGB so the linear value the shader sees is exactly 1.0.
        ReadOnlySpan<byte> pixel = [255, 255, 255, 255];
        var result = RegisterTexture("__white", 1, 1, TextureFormat.Rgba8Srgb, pixel);
        return result.Match(
            ok: id => id,
            error: e => throw new InvalidOperationException($"Failed to register fallback white texture: {e.Message}"));
    }

    // ─── Texture upload (staging buffer + layout transitions) ─────────

    private unsafe (Image image, Allocation alloc, ImageView view) UploadTexture(
        int width, int height, TextureFormat format, ReadOnlySpan<byte> pixels)
    {
        var vkFormat = ToVkFormat(format);
        var size = (ulong)pixels.Length;

        // 1. Staging buffer in host-visible memory.
        var (staging, stagingAlloc) = _gpu.Allocator.AllocateBuffer(
            _gpu, size, BufferUsageFlags.TransferSrcBit, MemoryIntent.CpuToGpu);
        var mapped = _gpu.Allocator.Map(_gpu, stagingAlloc);
        fixed (byte* src = pixels)
            System.Buffer.MemoryCopy(src, mapped, (long)size, (long)size);
        _gpu.Allocator.Unmap(_gpu, stagingAlloc);

        // 2. Device-local image.
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = vkFormat,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        var (image, alloc) = _gpu.Allocator.AllocateImage(_gpu, in imageInfo, MemoryIntent.GpuOnly);

        // 3. Record + submit a one-shot command buffer:
        //    Undefined -> TransferDstOptimal, copy, TransferDstOptimal -> ShaderReadOnlyOptimal.
        var cb = BeginOneShotCommands();

        TransitionLayout(cb, image,
            ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
            AccessFlags.None, AccessFlags.TransferWriteBit,
            PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit);

        var copy = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D((uint)width, (uint)height, 1),
        };
        _gpu.Vk.CmdCopyBufferToImage(cb, staging, image, ImageLayout.TransferDstOptimal, 1, &copy);

        TransitionLayout(cb, image,
            ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            AccessFlags.TransferWriteBit, AccessFlags.ShaderReadBit,
            PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit);

        EndOneShotCommands(cb);

        // 4. Staging buffer can be freed now (queue is idle after EndOneShotCommands).
        _gpu.Allocator.DestroyBuffer(_gpu, staging, stagingAlloc);

        // 5. View.
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = vkFormat,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        if (_gpu.Vk.CreateImageView(_gpu.Device, &viewInfo, null, out var view) != Result.Success)
            throw new InvalidOperationException("Failed to create texture image view.");

        return (image, alloc, view);
    }

    private unsafe CommandBuffer BeginOneShotCommands()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _gpu.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        CommandBuffer cb;
        if (_gpu.Vk.AllocateCommandBuffers(_gpu.Device, &allocInfo, &cb) != Result.Success)
            throw new InvalidOperationException("Failed to allocate one-shot command buffer.");

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        _gpu.Vk.BeginCommandBuffer(cb, &beginInfo);
        return cb;
    }

    private unsafe void EndOneShotCommands(CommandBuffer cb)
    {
        _gpu.Vk.EndCommandBuffer(cb);

        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cb,
        };
        _gpu.Vk.QueueSubmit(_gpu.GraphicsQueue, 1, &submit, default);
        _gpu.Vk.QueueWaitIdle(_gpu.GraphicsQueue);
        _gpu.Vk.FreeCommandBuffers(_gpu.Device, _gpu.CommandPool, 1, &cb);
    }

    private unsafe void TransitionLayout(
        CommandBuffer cb, Image image,
        ImageLayout oldLayout, ImageLayout newLayout,
        AccessFlags srcAccess, AccessFlags dstAccess,
        PipelineStageFlags srcStage, PipelineStageFlags dstStage)
    {
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess,
        };
        _gpu.Vk.CmdPipelineBarrier(cb, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
    }

    private static Format ToVkFormat(TextureFormat f) => f switch
    {
        TextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
        TextureFormat.Rgba8Srgb => Format.R8G8B8A8Srgb,
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    // ─── Lifetime ──────────────────────────────────────────────────────

    public unsafe void Dispose()
    {
        foreach (var gpu in _meshGpu.Values)
        {
            VulkanBuffer.Destroy(_gpu, gpu.VertexBuffer, gpu.VertexAlloc);
            VulkanBuffer.Destroy(_gpu, gpu.IndexBuffer, gpu.IndexAlloc);
        }
        _meshGpu.Clear();
        _meshes.Clear();

        foreach (var gpu in _textureGpu.Values)
        {
            _gpu.Vk.DestroyImageView(_gpu.Device, gpu.View, null);
            _gpu.Allocator.DestroyImage(_gpu, gpu.Image, gpu.Alloc);
        }
        _textureGpu.Clear();
        _textures.Clear();
        _materials.Clear();

        if (_sharedSampler.Handle != 0)
        {
            _gpu.Vk.DestroySampler(_gpu.Device, _sharedSampler, null);
            _sharedSampler = default;
        }
    }
}
