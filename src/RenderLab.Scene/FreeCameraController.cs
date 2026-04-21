using System.Numerics;

namespace RenderLab.Scene;

/// <summary>
/// Immutable free-fly camera state. Position and orientation are independent —
/// yaw/pitch rotate in place, <see cref="CameraInput.MoveDelta"/> translates
/// along the camera's local axes.
/// </summary>
public readonly record struct FreeCameraState(
    Vector3 Position,
    float Yaw,
    float Pitch,
    float FovRadians,
    float NearPlane,
    float FarPlane);

/// <summary>
/// Input deltas consumed by <see cref="FreeCameraController"/>.
/// <see cref="MoveDelta"/> is in camera-local axes: X=right, Y=up, Z=forward.
/// </summary>
public readonly record struct CameraInput(
    float YawDelta,
    float PitchDelta,
    Vector3 MoveDelta);

/// <summary>
/// Pure free-fly camera controller. All methods are static, side-effect-free.
/// Takes previous state + input, returns new state.
/// </summary>
public static class FreeCameraController
{
    private const float MinPitch = -MathF.PI / 2f + 0.01f;
    private const float MaxPitch = MathF.PI / 2f - 0.01f;

    public static FreeCameraState CreateDefault() => new(
        Position: new Vector3(2.1f, 1.85f, 2.1f),
        Yaw: MathF.PI / 4f,
        Pitch: -0.55f,
        FovRadians: MathF.PI / 4f,
        NearPlane: 0.1f,
        FarPlane: 100f);

    public static FreeCameraState Update(FreeCameraState state, CameraInput input)
    {
        float yaw = state.Yaw + input.YawDelta;
        float pitch = Math.Clamp(state.Pitch + input.PitchDelta, MinPitch, MaxPitch);

        var position = state.Position;
        if (input.MoveDelta != Vector3.Zero)
        {
            var forward = GetForward(yaw, pitch);
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));
            position += right * input.MoveDelta.X + up * input.MoveDelta.Y + forward * input.MoveDelta.Z;
        }

        return state with
        {
            Position = position,
            Yaw = yaw,
            Pitch = pitch,
        };
    }

    public static Camera ToCamera(FreeCameraState state, float aspectRatio)
    {
        var forward = GetForward(state.Yaw, state.Pitch);

        return new Camera(
            Position: state.Position,
            Target: state.Position + forward,
            Up: Vector3.UnitY,
            FovRadians: state.FovRadians,
            AspectRatio: aspectRatio,
            NearPlane: state.NearPlane,
            FarPlane: state.FarPlane);
    }

    // yaw=0, pitch=0 looks down -Z. Positive pitch looks up; positive yaw turns right.
    private static Vector3 GetForward(float yaw, float pitch)
    {
        float cosPitch = MathF.Cos(pitch);
        return new Vector3(
            -MathF.Sin(yaw) * cosPitch,
             MathF.Sin(pitch),
            -MathF.Cos(yaw) * cosPitch);
    }
}
