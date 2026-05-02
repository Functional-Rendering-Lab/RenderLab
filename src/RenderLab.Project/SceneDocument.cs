using System.Text.Json.Serialization;

namespace RenderLab.Project;

/// <summary>
/// Pure document form of a single scene file (<c>*.scene.json</c>). Mirrors
/// the runtime <c>UiModel</c> + the asset records the scene needs, but holds
/// only plain data (primitive arrays + tagged unions). The <see cref="Project"/>
/// loader projects this into runtime types; the saver is the inverse.
/// </summary>
public sealed record SceneDocument(
    int Version,
    CameraDoc Camera,
    AmbientDoc Ambient,
    LightDoc[] Lights,
    RenderConfigDoc RenderConfig,
    SceneAssetsDoc Assets,
    DrawableDoc[] Drawables);

public sealed record CameraDoc(float[] Position, float[] Target, float FovDeg);

public sealed record AmbientDoc(float[] Sky, float[] Ground);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PointLightDoc), "point")]
[JsonDerivedType(typeof(DirectionalLightDoc), "directional")]
public abstract record LightDoc;

public sealed record PointLightDoc(float[] Position, float[] Color, float Intensity) : LightDoc;

public sealed record DirectionalLightDoc(float[] Direction, float[] Color, float Intensity) : LightDoc;

/// <summary>
/// Per-scene rendering knobs not tied to any one drawable: shading mode,
/// debug-only overrides, clear colour. Mirrors the editable fields in
/// <c>UiModel</c> that the deferred pipeline reads.
/// </summary>
public sealed record RenderConfigDoc(string Shading, bool LightingOnly, string Viz, float[] ClearColor);

/// <summary>
/// Asset definitions scoped to one scene. Materials live inline (they're
/// scene-scoped parameter bundles, not file-backed). Meshes and textures
/// declare a <see cref="AssetSourceDoc"/> — either symbolic procedural or a
/// project-relative file path. Cross-references in <see cref="DrawableDoc"/>
/// are by index into these arrays.
/// </summary>
public sealed record SceneAssetsDoc(
    MeshEntryDoc[] Meshes,
    TextureEntryDoc[] Textures,
    MaterialDoc[] Materials);

public sealed record MeshEntryDoc(string Name, AssetSourceDoc Source);

public sealed record TextureEntryDoc(string Name, AssetSourceDoc Source);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ProceduralSourceDoc), "procedural")]
[JsonDerivedType(typeof(FileSourceDoc), "file")]
public abstract record AssetSourceDoc;

/// <summary>
/// Symbolic generator reference — the loader resolves the
/// <see cref="Generator"/> name + <see cref="Params"/> via a procedural-asset
/// source registered with the runtime catalog. Saving doesn't materialize the
/// pixels/vertices to disk; the generator name is the source of truth.
/// </summary>
public sealed record ProceduralSourceDoc(string Generator, Dictionary<string, System.Text.Json.JsonElement>? Params)
    : AssetSourceDoc;

/// <summary>
/// Project-relative file path. <see cref="Path"/> may carry an optional
/// <c>#suffix</c> to select a specific resource inside a multi-resource file
/// (e.g. <c>assets/box.glb#mesh0</c>).
/// </summary>
public sealed record FileSourceDoc(string Path) : AssetSourceDoc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BlinnPhongMaterialDoc), "blinnPhong")]
public abstract record MaterialDoc(string Name);

/// <summary>Blinn-Phong material asset. <see cref="AlbedoMap"/> is an index into
/// the scene's <see cref="SceneAssetsDoc.Textures"/> array, or <c>null</c> for
/// untextured.</summary>
public sealed record BlinnPhongMaterialDoc(
    string Name,
    float[] Albedo,
    float SpecularStrength,
    float Shininess,
    int? AlbedoMap) : MaterialDoc(Name);

/// <summary>
/// Drawable seed: indices into the scene's <see cref="SceneAssetsDoc.Meshes"/>
/// and <see cref="SceneAssetsDoc.Materials"/> arrays plus a placement.
/// </summary>
public sealed record DrawableDoc(string Name, int Mesh, int Material, TransformDoc Transform);

/// <summary><see cref="Rotation"/> is a quaternion <c>[x, y, z, w]</c>. <see cref="Scale"/>
/// is uniform (matches the engine's <c>Transform</c>).</summary>
public sealed record TransformDoc(float[] Position, float[] Rotation, float Scale);
