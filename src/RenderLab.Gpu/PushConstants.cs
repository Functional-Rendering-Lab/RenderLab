using System.Numerics;
using System.Runtime.InteropServices;

namespace RenderLab.Gpu;

[StructLayout(LayoutKind.Sequential)]
public struct GBufferPushConstants
{
    public Matrix4x4 Model;
    public Matrix4x4 ViewProj;
}

[StructLayout(LayoutKind.Sequential)]
public struct LightingPushConstants
{
    public Vector4 CameraPos;
    public Vector4 LightPos;
    public Vector4 LightColor; // rgb = color, a = intensity
}

[StructLayout(LayoutKind.Sequential)]
public struct DebugVizPushConstants
{
    public int Mode;       // 0 = rgb passthrough, 1 = depth
    public float NearPlane;
    public float FarPlane;
}
