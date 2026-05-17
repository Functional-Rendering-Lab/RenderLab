using RenderLab.Functional;

namespace RenderLab.Scene;

/// <summary>
/// Near/far clip plane pair for a perspective camera. Invariants: both finite,
/// <c>0 &lt; Near &lt; Far</c>.
/// </summary>
public readonly record struct ClipPlanes
{
    public float Near { get; }
    public float Far { get; }

    private ClipPlanes(float near, float far)
    {
        Near = near;
        Far = far;
    }

    public static Result<ClipPlanes, ValueError> Of(float near, float far)
    {
        if (!float.IsFinite(near) || !float.IsFinite(far))
            return Result.Error<ClipPlanes, ValueError>(new ValueError.NotFinite("ClipPlanes"));
        if (near <= 0f || far <= near)
            return Result.Error<ClipPlanes, ValueError>(
                new ValueError.OutOfRange("ClipPlanes", near, $"0 < near < far (got near={near}, far={far})"));
        return Result.Ok<ClipPlanes, ValueError>(new ClipPlanes(near, far));
    }

    public static ClipPlanes UnsafeFrom(float near, float far) => new(near, far);
}
