namespace RenderLab.Functional;

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
