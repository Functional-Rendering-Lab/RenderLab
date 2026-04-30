using System.Numerics;
using System.Runtime.InteropServices;

namespace RenderLab.Gpu;

/// <summary>
/// Per-draw constants for the G-Buffer geometry pass. Layout must match the
/// <c>layout(push_constant)</c> block in <c>gbuffer.vert</c> / <c>gbuffer.frag</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GBufferPushConstants
{
    public Matrix4x4 Model;
    public Matrix4x4 ViewProj;
    public Vector3 Albedo;
    public float SpecularStrength;
    public float Shininess;
}

/// <summary>
/// Per-frame constants for the deferred lighting fullscreen pass. Per-light data
/// (position, color, intensity) lives in the lighting SSBO at set 1, binding 0;
/// only the count crosses through here. Layout must match <c>lighting.frag</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LightingPushConstants
{
    public Vector4 CameraPos;
    public int ShadingMode;    // 0 = Lambertian, 1 = Phong, 2 = Blinn-Phong
    public int LightingOnly;   // 1 = emit unfiltered light (no albedo, no ambient)
    public int LightCount;
}

/// <summary>
/// Constants for the debug visualization fullscreen pass. <see cref="Mode"/>
/// selects between RGB passthrough and linearized-depth display.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DebugVizPushConstants
{
    public int Mode;       // 0 = rgb passthrough, 1 = depth
    public float NearPlane;
    public float FarPlane;
}
