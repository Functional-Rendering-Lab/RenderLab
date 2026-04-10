using System.Globalization;
using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Minimal Wavefront OBJ parser. Supports positions (v), normals (vn), texture coords (vt),
/// and triangulated faces (f). N-gon faces are fan-triangulated. Materials and groups are ignored.
/// </summary>
public static class ObjLoader
{
    /// <summary>
    /// Loads an OBJ file into a <see cref="MeshData"/> with de-duplicated vertices.
    /// Falls back to <see cref="Vector3.UnitY"/> for missing normals and <see cref="Vector2.Zero"/> for missing UVs.
    /// </summary>
    public static MeshData Load(string path)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();

        var vertices = new List<Vertex3D>();
        var indices = new List<uint>();
        var vertexCache = new Dictionary<(int p, int n, int t), uint>();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    positions.Add(new Vector3(
                        ParseFloat(parts[1]),
                        ParseFloat(parts[2]),
                        ParseFloat(parts[3])));
                    break;

                case "vn" when parts.Length >= 4:
                    normals.Add(new Vector3(
                        ParseFloat(parts[1]),
                        ParseFloat(parts[2]),
                        ParseFloat(parts[3])));
                    break;

                case "vt" when parts.Length >= 3:
                    uvs.Add(new Vector2(
                        ParseFloat(parts[1]),
                        ParseFloat(parts[2])));
                    break;

                case "f" when parts.Length >= 4:
                    // Triangulate fan: 0-1-2, 0-2-3, 0-3-4, ...
                    var faceIndices = new uint[parts.Length - 1];
                    for (int i = 1; i < parts.Length; i++)
                        faceIndices[i - 1] = ResolveVertex(
                            parts[i], positions, normals, uvs,
                            vertices, vertexCache);

                    for (int i = 2; i < faceIndices.Length; i++)
                    {
                        indices.Add(faceIndices[0]);
                        indices.Add(faceIndices[i - 1]);
                        indices.Add(faceIndices[i]);
                    }
                    break;
            }
        }

        return new MeshData([.. vertices], [.. indices]);
    }

    private static uint ResolveVertex(
        string face,
        List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs,
        List<Vertex3D> vertices, Dictionary<(int, int, int), uint> cache)
    {
        var parts = face.Split('/');
        int pi = int.Parse(parts[0]) - 1;
        int ti = parts.Length > 1 && parts[1].Length > 0 ? int.Parse(parts[1]) - 1 : -1;
        int ni = parts.Length > 2 && parts[2].Length > 0 ? int.Parse(parts[2]) - 1 : -1;

        var key = (pi, ni, ti);
        if (cache.TryGetValue(key, out var existing))
            return existing;

        var pos = positions[pi];
        var normal = ni >= 0 ? normals[ni] : Vector3.UnitY;
        var uv = ti >= 0 ? uvs[ti] : Vector2.Zero;

        uint index = (uint)vertices.Count;
        vertices.Add(new Vertex3D(pos, normal, uv));
        cache[key] = index;
        return index;
    }

    /// <summary>Generates a unit cube centered at origin with correct normals and UVs per face.</summary>
    public static MeshData CreateCube()
    {
        var verts = new List<Vertex3D>();
        var idx = new List<uint>();

        // 6 faces, each with unique normals
        // right × up must equal normal for consistent CCW winding
        ReadOnlySpan<(Vector3 normal, Vector3 right, Vector3 up)> faces =
        [
            ( Vector3.UnitZ,  Vector3.UnitX, Vector3.UnitY),   // front:  X × Y =  Z ✓
            (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),   // back: -X × Y = -Z ✓
            ( Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),   // right:-Z × Y =  X ✓
            (-Vector3.UnitX,  Vector3.UnitZ, Vector3.UnitY),   // left:  Z × Y = -X ✓
            ( Vector3.UnitY,  Vector3.UnitX, -Vector3.UnitZ),  // top:   X ×-Z =  Y ✓
            (-Vector3.UnitY,  Vector3.UnitX,  Vector3.UnitZ),  // bottom:X × Z = -Y ✓
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

    private static float ParseFloat(string s) =>
        float.Parse(s, CultureInfo.InvariantCulture);
}
