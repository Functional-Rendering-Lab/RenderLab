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

    public static ClipPlanes Of(float near, float far)
    {
        if (!float.IsFinite(near) || !float.IsFinite(far) || near <= 0f || far <= near)
            throw new ArgumentException($"ClipPlanes require 0 < near < far (got near={near}, far={far}).");
        return new ClipPlanes(near, far);
    }

    public static ClipPlanes UnsafeFrom(float near, float far) => new(near, far);
}
