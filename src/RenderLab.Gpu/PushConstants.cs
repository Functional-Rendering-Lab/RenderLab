using System.Numerics;
using System.Runtime.InteropServices;

namespace RenderLab.Gpu;

/// <summary>
/// Per-draw constants for the G-Buffer geometry pass. Layout must match the
/// <c>layout(push_constant)</c> block in <c>gbuffer.vert</c> / <c>gbuffer.frag</c>.
/// View-projection lives in a per-frame UBO (set=1, binding=0) instead of
/// here, so the block stays at 84 bytes — under the 128-byte
/// <c>maxPushConstantSize</c> guaranteed by every Vulkan implementation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GBufferPushConstants
{
    public Matrix4x4 Model;
    public Vector3 Albedo;
    public float SpecularStrength;
    public float Shininess;
}

/// <summary>
/// Per-frame constants for the deferred lighting fullscreen pass. Per-light data
/// (position/direction, color, intensity, type tag) lives in the lighting SSBO
/// at set 1, binding 0; only the count crosses through here. <see cref="Flags"/>
/// packs four ints (shading mode, lighting-only, light count, background mode)
/// into a single <see cref="Vector4"/> so the whole struct stays at 112 bytes —
/// under the 128-byte <c>maxPushConstantSize</c> guaranteed by every Vulkan
/// implementation. The shader reads the lanes back with <c>int(flags.x)</c> etc.
/// Layout matches the <c>layout(push_constant)</c> block in <c>lighting.frag</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LightingPushConstants
{
    public Vector4 CameraPos;
    // x = shadingMode, y = lightingOnly, z = lightCount, w = backgroundMode.
    public Vector4 Flags;
    public Vector4 AmbientSky;
    public Vector4 AmbientGround;
    // View basis for reconstructing world-space ray direction in the background.
    // xyz of CamRight/CamUp are pre-scaled by tan(fovX/2) and tan(fovY/2) so the
    // shader can do `forward + ndc.x * right - ndc.y * up` and normalize.
    public Vector4 CamRight;
    public Vector4 CamUp;
    public Vector4 CamForward;
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
