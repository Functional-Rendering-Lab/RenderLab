using System.Numerics;
using System.Text.Json;

namespace RenderLab.Assets;

/// <summary>
/// Built-in procedural generators the engine ships with: <c>sphere</c>,
/// <c>cube</c>, <c>checker</c>. Each accepts an optional params dictionary
/// (e.g. <c>{"stacks":32,"slices":32}</c> for sphere). New generators are
/// added by registering an additional <see cref="IProceduralAssetSource"/>
/// implementation.
/// </summary>
public sealed class DefaultProceduralAssets : IProceduralAssetSource
{
    public MeshData? TryCreateMesh(string generator, IReadOnlyDictionary<string, JsonElement>? @params) =>
        generator switch
        {
            "sphere" => CreateSphere(
                stacks: ReadInt(@params, "stacks", 32),
                slices: ReadInt(@params, "slices", 32)),
            "cube"   => CreateCube(),
            _        => null,
        };

    public ProceduralTexture? TryCreateTexture(string generator, IReadOnlyDictionary<string, JsonElement>? @params) =>
        generator switch
        {
            "checker" => CreateChecker(
                width:  ReadInt(@params, "size", 256),
                height: ReadInt(@params, "size", 256),
                cells:  ReadInt(@params, "cells", 8),
                format: ReadFormat(@params, "format", TextureFormat.Rgba8Srgb)),
            _ => null,
        };

    // ─── Generators ───────────────────────────────────────────────────

    private static MeshData CreateCube()
    {
        var verts = new List<Vertex3D>();
        var idx = new List<uint>();
        ReadOnlySpan<(Vector3 normal, Vector3 right, Vector3 up)> faces =
        [
            ( Vector3.UnitZ,  Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),
            ( Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),
            (-Vector3.UnitX,  Vector3.UnitZ, Vector3.UnitY),
            ( Vector3.UnitY,  Vector3.UnitX, -Vector3.UnitZ),
            (-Vector3.UnitY,  Vector3.UnitX,  Vector3.UnitZ),
        ];
        foreach (var (n, r, u) in faces)
        {
            uint b = (uint)verts.Count;
            var center = n * 0.5f;
            verts.Add(new Vertex3D(center - r * 0.5f - u * 0.5f, n, new Vector2(0, 0)));
            verts.Add(new Vertex3D(center + r * 0.5f - u * 0.5f, n, new Vector2(1, 0)));
            verts.Add(new Vertex3D(center + r * 0.5f + u * 0.5f, n, new Vector2(1, 1)));
            verts.Add(new Vertex3D(center - r * 0.5f + u * 0.5f, n, new Vector2(0, 1)));
            idx.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
        }
        return new MeshData([.. verts], [.. idx]);
    }

    private static MeshData CreateSphere(int stacks, int slices)
    {
        var verts = new List<Vertex3D>();
        var idx = new List<uint>();
        for (int i = 0; i <= stacks; i++)
        {
            float phi = MathF.PI * i / stacks;
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);
            float v = (float)i / stacks;
            for (int j = 0; j <= slices; j++)
            {
                float theta = 2f * MathF.PI * j / slices;
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);
                float u = (float)j / slices;
                var normal = new Vector3(sinPhi * cosTheta, cosPhi, sinPhi * sinTheta);
                verts.Add(new Vertex3D(normal, normal, new Vector2(u, v)));
            }
        }
        int ringSize = slices + 1;
        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint tl = (uint)(i * ringSize + j);
                uint tr = tl + 1;
                uint bl = (uint)((i + 1) * ringSize + j);
                uint br = bl + 1;
                idx.AddRange([tl, tr, bl]);
                idx.AddRange([tr, br, bl]);
            }
        }
        return new MeshData([.. verts], [.. idx]);
    }

    private static ProceduralTexture CreateChecker(int width, int height, int cells, TextureFormat format)
    {
        var pixels = new byte[width * height * 4];
        int cellW = Math.Max(1, width / cells);
        int cellH = Math.Max(1, height / cells);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool dark = ((x / cellW) + (y / cellH)) % 2 == 0;
                int o = (y * width + x) * 4;
                byte vByte = dark ? (byte)40 : (byte)220;
                pixels[o + 0] = vByte;
                pixels[o + 1] = vByte;
                pixels[o + 2] = vByte;
                pixels[o + 3] = 255;
            }
        }
        return new ProceduralTexture(width, height, format, pixels);
    }

    // ─── Param helpers ────────────────────────────────────────────────

    private static int ReadInt(IReadOnlyDictionary<string, JsonElement>? @params, string key, int fallback) =>
        @params is not null && @params.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetInt32()
            : fallback;

    private static TextureFormat ReadFormat(IReadOnlyDictionary<string, JsonElement>? @params, string key, TextureFormat fallback)
    {
        if (@params is null || !@params.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
            return fallback;
        return el.GetString()?.ToLowerInvariant() switch
        {
            "rgba8unorm" => TextureFormat.Rgba8Unorm,
            "rgba8srgb"  => TextureFormat.Rgba8Srgb,
            _            => fallback,
        };
    }
}
