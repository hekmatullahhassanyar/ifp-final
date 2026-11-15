namespace DeliveryQuote;

public sealed record DomainError(string Code, string Message);

public abstract record Result<T>
{
    private Result() { }

    public sealed record Success(T Value) : Result<T>;
    public sealed record Failure(DomainError Error) : Result<T>;
}

public static class Result
{
    public static Result<T> Ok<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>.Success(value);
    }

    public static Result<T> Fail<T>(string code, string message) =>
        new Result<T>.Failure(new DomainError(code, message));

    public static Result<T> Fail<T>(DomainError error) => new Result<T>.Failure(error);
}

public abstract record Maybe<T>
{
    private Maybe() { }

    public sealed record Some(T Value) : Maybe<T>;
    public sealed record None : Maybe<T>;
}

public static class Maybe
{
    public static Maybe<T> Some<T>(T value) => new Maybe<T>.Some(value);
    public static Maybe<T> None<T>() => new Maybe<T>.None();
}

public static class ResultExtensions
{
    public static Result<TOut> Map<T, TOut>(this Result<T> result, Func<T, TOut> f) => result switch
    {
        Result<T>.Success s => Result.Ok(f(s.Value)),
        Result<T>.Failure fail => Result.Fail<TOut>(fail.Error),
        _ => throw new InvalidOperationException("Unknown Result case.")
    };

    public static Result<TOut> Bind<T, TOut>(this Result<T> result, Func<T, Result<TOut>> f) => result switch
    {
        Result<T>.Success s => f(s.Value),
        Result<T>.Failure fail => Result.Fail<TOut>(fail.Error),
        _ => throw new InvalidOperationException("Unknown Result case.")
    };

    public static TOut Match<T, TOut>(this Result<T> result, Func<T, TOut> onSuccess, Func<DomainError, TOut> onFailure) =>
        result switch
        {
            Result<T>.Success s => onSuccess(s.Value),
            Result<T>.Failure fail => onFailure(fail.Error),
            _ => throw new InvalidOperationException("Unknown Result case.")
        };

    public static Result<T> ToResult<T>(this Maybe<T> maybe, DomainError whenMissing) => maybe switch
    {
        Maybe<T>.Some s => Result.Ok(s.Value),
        Maybe<T>.None => Result.Fail<T>(whenMissing),
        _ => throw new InvalidOperationException("Unknown Maybe case.")
    };
}
