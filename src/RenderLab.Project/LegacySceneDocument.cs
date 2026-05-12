using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenderLab.Project;

/// <summary>
/// Pre-AssetRef shape of <c>*.scene.json</c>. Kept around so the loader can
/// recognise old files on disk and run them through <see cref="SceneUpgrader"/>.
/// New scenes write the AssetRef-based <see cref="SceneDocument"/> shape.
/// </summary>
public sealed record LegacySceneDocument(
    int Version,
    CameraDoc Camera,
    AmbientDoc Ambient,
    LightDoc[] Lights,
    RenderConfigDoc RenderConfig,
    LegacySceneAssetsDoc Assets,
    LegacyDrawableDoc[] Drawables);

public sealed record LegacySceneAssetsDoc(
    LegacyMeshEntryDoc[] Meshes,
    LegacyTextureEntryDoc[] Textures,
    LegacyMaterialDoc[] Materials);

public sealed record LegacyMeshEntryDoc(string Name, LegacyAssetSourceDoc Source);

public sealed record LegacyTextureEntryDoc(string Name, LegacyAssetSourceDoc Source);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LegacyProceduralSourceDoc), "procedural")]
[JsonDerivedType(typeof(LegacyFileSourceDoc), "file")]
public abstract record LegacyAssetSourceDoc;

public sealed record LegacyProceduralSourceDoc(
    string Generator,
    Dictionary<string, JsonElement>? Params) : LegacyAssetSourceDoc;

public sealed record LegacyFileSourceDoc(string Path) : LegacyAssetSourceDoc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LegacyBlinnPhongMaterialDoc), "blinnPhong")]
public abstract record LegacyMaterialDoc(string Name);

public sealed record LegacyBlinnPhongMaterialDoc(
    string Name,
    float[] Albedo,
    float SpecularStrength,
    float Shininess,
    int? AlbedoMap) : LegacyMaterialDoc(Name);

public sealed record LegacyDrawableDoc(string Name, int Mesh, int Material, TransformDoc Transform);
