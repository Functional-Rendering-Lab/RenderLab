using System.Collections.Immutable;
using System.Numerics;
using SharpGLTF.Schema2;
using StbImageSharp;

namespace RenderLab.Assets;

/// <summary>
/// Pure parser from a glTF / glb file into a <see cref="GltfImport"/>
/// blueprint. Decodes vertex buffers into <see cref="Vertex3D"/>, decodes PNG
/// /JPEG textures via StbImageSharp, and maps glTF's PBR baseColor onto the
/// engine's Blinn-Phong material. Node transforms are decomposed into
/// translation + rotation + uniform scale; non-uniform scale collapses to the
/// X component (lab limitation, flag if a real asset hits it).
/// </summary>
public static class GltfLoader
{
    public static GltfImport Load(string path)
    {
        var model = ModelRoot.Load(path);

        // ─── Textures: decode every distinct image to RGBA8 ───────────
        var textures = ImmutableArray.CreateBuilder<GltfTextureBlueprint>();
        var imageIndexToTextureIndex = new Dictionary<int, int>();
        for (int i = 0; i < model.LogicalImages.Count; i++)
        {
            var img = model.LogicalImages[i];
            var bytes = img.Content.Content.ToArray();
            var decoded = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            textures.Add(new GltfTextureBlueprint(
                Name: string.IsNullOrEmpty(img.Name) ? $"image_{i}" : img.Name,
                Width: decoded.Width,
                Height: decoded.Height,
                // Albedo maps decode through sRGB. Other roles (normal, ORM)
                // would need Rgba8Unorm — wire that when those roles land.
                Format: TextureFormat.Rgba8Srgb,
                Pixels: decoded.Data));
            imageIndexToTextureIndex[i] = i;
        }

        // ─── Materials: PBR baseColor → Blinn-Phong ───────────────────
        var materials = ImmutableArray.CreateBuilder<GltfMaterialBlueprint>();
        for (int i = 0; i < model.LogicalMaterials.Count; i++)
        {
            var mat = model.LogicalMaterials[i];
            var baseColor = mat.FindChannel("BaseColor");
            var color = baseColor?.Color ?? new Vector4(0.6f, 0.6f, 0.6f, 1f);
            var albedoTexIndex = baseColor?.Texture?.PrimaryImage?.LogicalIndex ?? -1;
            // glTF roughness has no clean Blinn-Phong analogue; flat defaults
            // until the lab grows a PBR pass.
            materials.Add(new GltfMaterialBlueprint(
                Name: string.IsNullOrEmpty(mat.Name) ? $"material_{i}" : mat.Name,
                Albedo: new Vector3(color.X, color.Y, color.Z),
                SpecularStrength: 0.5f,
                Shininess: 32f,
                AlbedoTextureIndex: albedoTexIndex));
        }

        // ─── Meshes: each (mesh, primitive) becomes a registered entry ─
        // Track (meshIndex, primIndex) -> blueprint index so drawable
        // emission can reference the right entry per primitive.
        var meshes = ImmutableArray.CreateBuilder<GltfMeshBlueprint>();
        var primToMeshIndex = new Dictionary<(int meshIdx, int primIdx), int>();
        var primToMaterialIndex = new Dictionary<(int meshIdx, int primIdx), int>();
        for (int mi = 0; mi < model.LogicalMeshes.Count; mi++)
        {
            var m = model.LogicalMeshes[mi];
            for (int pi = 0; pi < m.Primitives.Count; pi++)
            {
                var prim = m.Primitives[pi];
                var data = ExtractPrimitive(prim);
                if (data is null) continue;

                var blueprintIndex = meshes.Count;
                meshes.Add(new GltfMeshBlueprint(
                    Name: string.IsNullOrEmpty(m.Name) ? $"mesh_{mi}_{pi}" : $"{m.Name}_{pi}",
                    Data: data));
                primToMeshIndex[(mi, pi)] = blueprintIndex;
                primToMaterialIndex[(mi, pi)] = prim.Material?.LogicalIndex ?? -1;
            }
        }

        // ─── Drawables: walk the default scene and emit one per primitive ─
        var drawables = ImmutableArray.CreateBuilder<GltfDrawableBlueprint>();
        var scene = model.DefaultScene ?? (model.LogicalScenes.Count > 0 ? model.LogicalScenes[0] : null);
        if (scene is not null)
        {
            foreach (var node in scene.VisualChildren)
                EmitDrawables(node, drawables, primToMeshIndex, primToMaterialIndex);
        }

        return new GltfImport(
            meshes.ToImmutable(),
            textures.ToImmutable(),
            materials.ToImmutable(),
            drawables.ToImmutable());
    }

    private static MeshData? ExtractPrimitive(MeshPrimitive prim)
    {
        var positions = prim.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (positions is null) return null;

        var normals = prim.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = prim.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var indices = prim.GetIndices();

        var vertices = new Vertex3D[positions.Count];
        for (int v = 0; v < positions.Count; v++)
        {
            var pos = positions[v];
            var nrm = normals is not null && v < normals.Count ? normals[v] : Vector3.UnitY;
            var uv  = uvs is not null && v < uvs.Count ? uvs[v] : Vector2.Zero;
            vertices[v] = new Vertex3D(pos, nrm, uv);
        }

        // glTF can omit indices for non-indexed draws; synthesise a 0..N-1 list.
        uint[] idx;
        if (indices is null)
        {
            idx = new uint[vertices.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = (uint)i;
        }
        else
        {
            idx = new uint[indices.Count];
            for (int i = 0; i < indices.Count; i++) idx[i] = indices[i];
        }

        return new MeshData(vertices, idx);
    }

    private static void EmitDrawables(
        Node node,
        ImmutableArray<GltfDrawableBlueprint>.Builder drawables,
        Dictionary<(int, int), int> primToMeshIndex,
        Dictionary<(int, int), int> primToMaterialIndex)
    {
        if (node.Mesh is { } mesh)
        {
            // Decompose the world matrix into translation + uniform scale.
            // Rotation is dropped (the engine Transform doesn't carry one yet);
            // non-uniform scale collapses to the X component.
            var world = node.WorldMatrix;
            Matrix4x4.Decompose(world, out var scale, out var rotation, out var translation);

            var mi = mesh.LogicalIndex;
            for (int pi = 0; pi < mesh.Primitives.Count; pi++)
            {
                if (!primToMeshIndex.TryGetValue((mi, pi), out var meshIndex)) continue;
                primToMaterialIndex.TryGetValue((mi, pi), out var matIndex);
                drawables.Add(new GltfDrawableBlueprint(
                    Name: string.IsNullOrEmpty(node.Name) ? $"{mesh.Name ?? "node"}_{pi}" : node.Name + (pi == 0 ? "" : $"_{pi}"),
                    MeshIndex: meshIndex,
                    MaterialIndex: matIndex,
                    Position: translation,
                    Rotation: rotation,
                    Scale: scale.X));
            }
        }

        foreach (var child in node.VisualChildren)
            EmitDrawables(child, drawables, primToMeshIndex, primToMaterialIndex);
    }
}
