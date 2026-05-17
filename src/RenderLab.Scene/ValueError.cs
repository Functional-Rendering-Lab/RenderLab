namespace RenderLab.Scene;

/// <summary>
/// Failure cases emitted by smart constructors when an invariant is violated.
/// Carried by <c>Result&lt;T, ValueError&gt;</c> so pure code never has to throw.
/// </summary>
public abstract record ValueError(string Message)
{
    public sealed record OutOfRange(string Field, float Got, string Expected)
        : ValueError($"{Field} out of range: got {Got}, expected {Expected}");

    public sealed record NotFinite(string Field)
        : ValueError($"{Field} must be finite");

    public sealed record ZeroVector(string Field)
        : ValueError($"{Field} must be non-zero");
}
