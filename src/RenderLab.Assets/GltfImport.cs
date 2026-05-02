using System.Collections.Immutable;
using System.Numerics;

namespace RenderLab.Assets;

/// <summary>
/// Pure result of <see cref="GltfLoader.Load"/>: index-keyed blueprints for
/// every asset extracted from a glTF document. Cross-references are by index
/// (e.g. <see cref="GltfMaterialBlueprint.AlbedoTextureIndex"/> points into
/// <see cref="Textures"/>). The shell walks the lists in dependency order
/// and rewrites indices into real registry ids.
/// </summary>
public sealed record GltfImport(
    ImmutableArray<GltfMeshBlueprint> Meshes,
    ImmutableArray<GltfTextureBlueprint> Textures,
    ImmutableArray<GltfMaterialBlueprint> Materials,
    ImmutableArray<GltfDrawableBlueprint> Drawables);

/// <summary>One glTF mesh primitive: indexed Vertex3D geometry + its glTF name.</summary>
public sealed record GltfMeshBlueprint(string Name, MeshData Data);

/// <summary>Decoded image bytes ready for GPU upload.</summary>
public sealed record GltfTextureBlueprint(
    string Name,
    int Width,
    int Height,
    TextureFormat Format,
    byte[] Pixels);

/// <summary>Material parameters with a texture cross-reference (-1 = none).</summary>
public sealed record GltfMaterialBlueprint(
    string Name,
    Vector3 Albedo,
    float SpecularStrength,
    float Shininess,
    int AlbedoTextureIndex);

/// <summary>One drawable seed: world-space placement + index references into
/// <see cref="GltfImport.Meshes"/> and <see cref="GltfImport.Materials"/>.
/// Kept as raw position/rotation/scale so this project doesn't have to depend
/// on <c>RenderLab.Scene</c> just for <c>Transform</c>.</summary>
public sealed record GltfDrawableBlueprint(
    string Name,
    int MeshIndex,
    int MaterialIndex,
    Vector3 Position,
    Quaternion Rotation,
    float Scale);
