using System.Collections.Immutable;

namespace RenderLab.Functional;

/// <summary>
/// A value-type result: either Ok(value) or Error(error).
/// </summary>
public readonly record struct Result<T, TError>
    where T : notnull
    where TError : notnull
{
    private readonly T? _value;
    private readonly TError? _error;
    public bool IsOk { get; }
    public bool IsError => !IsOk;

    private Result(T value)
    {
        _value = value;
        _error = default;
        IsOk = true;
    }

    private Result(TError error, bool _)
    {
        _value = default;
        _error = error;
        IsOk = false;
    }

    public static Result<T, TError> Ok(T value) => new(value);
    public static Result<T, TError> Error(TError error) => new(error, false);

    public TResult Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> error) =>
        IsOk ? ok(_value!) : error(_error!);

    public Result<TResult, TError> Map<TResult>(Func<T, TResult> map) where TResult : notnull =>
        IsOk ? Result<TResult, TError>.Ok(map(_value!)) : Result<TResult, TError>.Error(_error!);

    public Result<T, TNewError> MapError<TNewError>(Func<TError, TNewError> map) where TNewError : notnull =>
        IsOk ? Result<T, TNewError>.Ok(_value!) : Result<T, TNewError>.Error(map(_error!));

    public Result<TResult, TError> Bind<TResult>(Func<T, Result<TResult, TError>> bind) where TResult : notnull =>
        IsOk ? bind(_value!) : Result<TResult, TError>.Error(_error!);

    public T ValueOr(T fallback) => IsOk ? _value! : fallback;

    public T ValueOr(Func<TError, T> fallback) => IsOk ? _value! : fallback(_error!);

    public override string ToString() =>
        IsOk ? $"Ok({_value})" : $"Error({_error})";
}

public static class Result
{
    public static Result<T, TError> Ok<T, TError>(T value)
        where T : notnull where TError : notnull => Result<T, TError>.Ok(value);

    public static Result<T, TError> Error<T, TError>(TError error)
        where T : notnull where TError : notnull => Result<T, TError>.Error(error);

    /// <summary>
    /// Maps <paramref name="source"/> through <paramref name="f"/> and short-circuits
    /// on the first error. On success, the resulting array preserves input order;
    /// on failure, the first error is returned.
    /// </summary>
    public static Result<ImmutableArray<T>, TError> Traverse<TSource, T, TError>(
        IEnumerable<TSource> source, Func<TSource, Result<T, TError>> f)
        where T : notnull where TError : notnull
    {
        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (var s in source)
        {
            var r = f(s);
            if (r.IsError)
                return Result<ImmutableArray<T>, TError>.Error(r.Match(_ => default!, e => e));
            builder.Add(r.Match(x => x, _ => default!));
        }
        return Result<ImmutableArray<T>, TError>.Ok(builder.ToImmutable());
    }
}
