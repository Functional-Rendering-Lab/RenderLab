using System.Text.Json.Serialization;

namespace RenderLab.Project;

/// <summary>
/// Pure document form of a single scene file (<c>*.scene.json</c>). Holds
/// the scene-level state plus a flat drawable list. Asset definitions live
/// outside the scene — drawables reference meshes and materials by stable
/// <see cref="AssetRef"/> resolved against the project's
/// <see cref="AssetLibrary"/>.
/// </summary>
public sealed record SceneDocument(
    int Version,
    CameraDoc Camera,
    AmbientDoc Ambient,
    LightDoc[] Lights,
    RenderConfigDoc RenderConfig,
    DrawableDoc[] Drawables);

/// <summary>Camera state matches <c>FreeCameraState</c>: yaw/pitch in degrees,
/// position in world space, vertical FOV in degrees.</summary>
public sealed record CameraDoc(float[] Position, float YawDeg, float PitchDeg, float FovDeg);

public sealed record AmbientDoc(float[] Sky, float[] Ground);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PointLightDoc), "point")]
[JsonDerivedType(typeof(DirectionalLightDoc), "directional")]
public abstract record LightDoc;

public sealed record PointLightDoc(float[] Position, float[] Color, float Intensity) : LightDoc;

public sealed record DirectionalLightDoc(float[] Direction, float[] Color, float Intensity) : LightDoc;

/// <summary>
/// Per-scene rendering knobs not tied to any one drawable.
/// </summary>
public sealed record RenderConfigDoc(
    string Shading,
    bool LightingOnly,
    string Viz,
    float[] ClearColor,
    string? Background = null);

/// <summary>
/// One placed instance: a mesh + material reference and a transform. Both
/// references are resolved against the project's <see cref="AssetLibrary"/>
/// at load time.
/// </summary>
public sealed record DrawableDoc(string Name, AssetRef Mesh, AssetRef Material, TransformDoc Transform);

public sealed record TransformDoc(float[] Position, float[] Rotation, float Scale);
