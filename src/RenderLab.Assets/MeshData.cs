namespace RenderLab.Assets;

/// <summary>
/// Immutable indexed mesh: a vertex buffer plus a 32-bit index buffer.
/// Produced by <see cref="ObjLoader"/> and uploaded to the GPU via
/// <c>VulkanBuffer.Create</c>.
/// </summary>
public sealed record MeshData(Vertex3D[] Vertices, uint[] Indices);
