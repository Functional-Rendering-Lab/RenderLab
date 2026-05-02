using RenderLab.Assets;
using Silk.NET.Vulkan;
using FResult = RenderLab.Functional.Result;
using FResultT = RenderLab.Functional.Result<RenderLab.Assets.MeshId, RenderLab.Assets.AssetError>;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace RenderLab.Gpu.Assets;

/// <summary>
/// The single owner of mesh assets and their backing GPU buffers. Implements the
/// pure <see cref="IAssetCatalog"/> view consumed by scene/UI code, and the
/// shell-side <see cref="IGpuAssetResolver"/> used by recording passes.
/// Counter starts at 1 so 0 is reserved for <see cref="MeshId.None"/>.
/// </summary>
public sealed class AssetRegistry : IAssetCatalog, IGpuAssetResolver, IDisposable
{
    private readonly GpuState _gpu;
    private readonly Dictionary<int, MeshAsset> _meshes = new();
    private readonly Dictionary<int, MeshGpu> _meshGpu = new();
    private int _nextId = 1;

    private readonly record struct MeshGpu(VkBuffer VertexBuffer, Allocation VertexAlloc, VkBuffer IndexBuffer, Allocation IndexAlloc, uint IndexCount);

    public AssetRegistry(GpuState gpu) => _gpu = gpu;

    public FResultT RegisterMesh(string name, MeshData data)
    {
        try
        {
            var (vb, va) = VulkanBuffer.Create<Vertex3D>(_gpu, BufferUsageFlags.VertexBufferBit, data.Vertices.AsSpan());
            var (ib, ia) = VulkanBuffer.Create<uint>(_gpu, BufferUsageFlags.IndexBufferBit, data.Indices.AsSpan());

            var id = new MeshId(_nextId++);
            _meshes[id.Value] = new MeshAsset(id, name, data);
            _meshGpu[id.Value] = new MeshGpu(vb, va, ib, ia, (uint)data.Indices.Length);
            return FResult.Ok<MeshId, AssetError>(id);
        }
        catch (Exception ex)
        {
            return FResult.Error<MeshId, AssetError>(new AssetError.GpuUploadFailed(ex.Message));
        }
    }

    public FResultT LoadMesh(string path)
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

    public void Dispose()
    {
        foreach (var gpu in _meshGpu.Values)
        {
            VulkanBuffer.Destroy(_gpu, gpu.VertexBuffer, gpu.VertexAlloc);
            VulkanBuffer.Destroy(_gpu, gpu.IndexBuffer, gpu.IndexAlloc);
        }
        _meshGpu.Clear();
        _meshes.Clear();
    }
}
