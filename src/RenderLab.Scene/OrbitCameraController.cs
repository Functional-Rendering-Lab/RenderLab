using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Immutable orbital camera state. Spherical coordinates around a target point.
/// Use <see cref="OrbitCameraController"/> to produce updated states from input.
/// </summary>
public readonly record struct OrbitState(
    Vector3 Target,
    float Distance,
    float Yaw,
    float Pitch,
    float FovRadians,
    float NearPlane,
    float FarPlane);

/// <summary>
/// Input deltas consumed by the orbit controller. Platform-agnostic — the desktop
/// layer maps mouse events to this, XR could map controller stick, etc.
/// </summary>
public readonly record struct CameraInput(
    float YawDelta,
    float PitchDelta,
    float ZoomDelta,
    Vector3 PanDelta);

/// <summary>
/// Pure orbit camera controller. All methods are static, side-effect-free.
/// Takes previous state + input, returns new state.
/// </summary>
public static class OrbitCameraController
{
    private const float MinDistance = 0.3f;
    private const float MaxDistance = 50f;
    private const float MinPitch = -MathF.PI / 2f + 0.01f;
    private const float MaxPitch = MathF.PI / 2f - 0.01f;

    // Aligned with the default key light at (2,3,2) so the spec highlight lands near the middle of the visible sphere.
    public static OrbitState CreateDefault() => new(
        Target: Vector3.Zero,
        Distance: 3.5f,
        Yaw: MathF.PI / 4f,
        Pitch: 0.55f,
        FovRadians: MathF.PI / 4f,
        NearPlane: 0.1f,
        FarPlane: 100f);

    public static OrbitState Update(OrbitState state, CameraInput input)
    {
        float yaw = state.Yaw + input.YawDelta;
        float pitch = Math.Clamp(state.Pitch + input.PitchDelta, MinPitch, MaxPitch);
        float distance = Math.Clamp(state.Distance - input.ZoomDelta, MinDistance, MaxDistance);

        // Pan in the camera's local XY plane
        var target = state.Target;
        if (input.PanDelta != Vector3.Zero)
        {
            var right = GetRight(yaw, pitch);
            var up = GetUp(yaw, pitch);
            target += right * input.PanDelta.X + up * input.PanDelta.Y;
        }

        return state with
        {
            Target = target,
            Distance = distance,
            Yaw = yaw,
            Pitch = pitch,
        };
    }

    public static Camera ToCamera(OrbitState state, float aspectRatio)
    {
        var position = state.Target + GetDirection(state.Yaw, state.Pitch) * state.Distance;

        return new Camera(
            Position: position,
            Target: state.Target,
            Up: Vector3.UnitY,
            FovRadians: state.FovRadians,
            AspectRatio: aspectRatio,
            NearPlane: state.NearPlane,
            FarPlane: state.FarPlane);
    }

    private static Vector3 GetDirection(float yaw, float pitch)
    {
        float cosPitch = MathF.Cos(pitch);
        return new Vector3(
            MathF.Sin(yaw) * cosPitch,
            MathF.Sin(pitch),
            MathF.Cos(yaw) * cosPitch);
    }

    private static Vector3 GetRight(float yaw, float pitch)
    {
        var forward = -GetDirection(yaw, pitch);
        return Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
    }

    private static Vector3 GetUp(float yaw, float pitch)
    {
        var forward = -GetDirection(yaw, pitch);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        return Vector3.Normalize(Vector3.Cross(right, forward));
    }
}
