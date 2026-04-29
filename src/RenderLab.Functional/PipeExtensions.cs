namespace RenderLab.Functional;

/// <summary>
/// Forward-pipe helpers for building left-to-right transformation chains in
/// C#. <see cref="Pipe"/> threads a value through a transform; <see cref="PipeAction"/>
/// runs a side-effect against a value and returns it unchanged.
/// </summary>
public static class PipeExtensions
{
    public static TResult Pipe<T, TResult>(this T value, Func<T, TResult> func) =>
        func(value);

    public static T PipeAction<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
