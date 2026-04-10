using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace RenderLab.Scene;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex3D(Vector3 position, Vector3 normal, Vector2 uv)
{
    public readonly Vector3 Position = position;
    public readonly Vector3 Normal = normal;
    public readonly Vector2 UV = uv;

    public static VertexInputBindingDescription BindingDescription => new()
    {
        Binding = 0,
        Stride = (uint)Marshal.SizeOf<Vertex3D>(),
        InputRate = VertexInputRate.Vertex,
    };

    public static VertexInputAttributeDescription[] AttributeDescriptions =>
    [
        new() // Position: vec3 at location 0
        {
            Binding = 0,
            Location = 0,
            Format = Format.R32G32B32Sfloat,
            Offset = 0,
        },
        new() // Normal: vec3 at location 1
        {
            Binding = 0,
            Location = 1,
            Format = Format.R32G32B32Sfloat,
            Offset = (uint)Marshal.SizeOf<Vector3>(),
        },
        new() // UV: vec2 at location 2
        {
            Binding = 0,
            Location = 2,
            Format = Format.R32G32Sfloat,
            Offset = (uint)(Marshal.SizeOf<Vector3>() * 2),
        },
    ];
}
