using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace RenderLab.Gpu;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex
{
    public readonly Vector2 Position;
    public readonly Vector3 Color;

    public Vertex(Vector2 position, Vector3 color)
    {
        Position = position;
        Color = color;
    }

    public static VertexInputBindingDescription BindingDescription => new()
    {
        Binding = 0,
        Stride = (uint)Marshal.SizeOf<Vertex>(),
        InputRate = VertexInputRate.Vertex,
    };

    public static VertexInputAttributeDescription[] AttributeDescriptions =>
    [
        new()
        {
            Binding = 0,
            Location = 0,
            Format = Format.R32G32Sfloat,
            Offset = 0,
        },
        new()
        {
            Binding = 0,
            Location = 1,
            Format = Format.R32G32B32Sfloat,
            Offset = (uint)Marshal.SizeOf<Vector2>(),
        },
    ];
}
