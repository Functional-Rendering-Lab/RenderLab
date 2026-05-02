using System.Collections.Immutable;
using System.Numerics;
using RenderLab.Assets;

namespace RenderLab.Gpu.Assets;

/// <summary>
/// Outcome of <see cref="AssetRegistry.ImportGltf"/>: registered ids for every
/// asset in dependency order, plus the resolved drawable seeds the demo can
/// dispatch as <c>UiMsg.AddDrawable</c>. Position + scale stay raw so this
/// type doesn't pull in <c>RenderLab.Scene</c> for <c>Transform</c>; the demo
/// constructs the transform when it dispatches.
/// </summary>
public sealed record GltfImportResult(
    ImmutableArray<MeshId> Meshes,
    ImmutableArray<TextureId> Textures,
    ImmutableArray<MaterialId> Materials,
    ImmutableArray<ImportedDrawable> Drawables);

public sealed record ImportedDrawable(
    string Name,
    MeshId Mesh,
    MaterialId Material,
    Vector3 Position,
    Quaternion Rotation,
    float Scale);
