namespace RenderLab.Functional;

/// <summary>
/// A value-type option: either Some(value) or None.
/// </summary>
public readonly record struct Optional<T> where T : notnull
{
    private readonly T? _value;
    public bool IsSome { get; }
    public bool IsNone => !IsSome;

    private Optional(T value)
    {
        _value = value;
        IsSome = true;
    }

    public static Optional<T> Some(T value) => new(value);
    public static Optional<T> None => default;

    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
        IsSome ? some(_value!) : none();

    public Optional<TResult> Map<TResult>(Func<T, TResult> map) where TResult : notnull =>
        IsSome ? Optional<TResult>.Some(map(_value!)) : Optional<TResult>.None;

    public Optional<TResult> Bind<TResult>(Func<T, Optional<TResult>> bind) where TResult : notnull =>
        IsSome ? bind(_value!) : Optional<TResult>.None;

    public T ValueOr(T fallback) => IsSome ? _value! : fallback;

    public T ValueOr(Func<T> fallback) => IsSome ? _value! : fallback();

    public override string ToString() =>
        IsSome ? $"Some({_value})" : "None";
}

public static class Optional
{
    public static Optional<T> Some<T>(T value) where T : notnull => Optional<T>.Some(value);
    public static Optional<T> None<T>() where T : notnull => Optional<T>.None;
}
