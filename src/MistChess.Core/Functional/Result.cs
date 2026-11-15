namespace MistChess.Core.Functional;

public abstract record Result<TValue, TError>
{
    protected Result() { }

    public sealed record Ok(TValue Value) : Result<TValue, TError>;
    public sealed record Err(TError Error) : Result<TValue, TError>;

    public static Result<TValue, TError> Success(TValue value) => new Ok(value);
    public static Result<TValue, TError> Failure(TError error) => new Err(error);

    public bool IsSuccess => this is Ok;
    public bool IsFailure => this is Err;

    public TResult Match<TResult>(Func<TValue, TResult> success, Func<TError, TResult> failure) => this switch
    {
        Ok value => success(value.Value),
        Err error => failure(error.Error),
        _ => throw new InvalidOperationException("Unknown Result case.")
    };

    public Result<TResult, TError> Map<TResult>(Func<TValue, TResult> map) => this switch
    {
        Ok value => Result<TResult, TError>.Success(map(value.Value)),
        Err error => Result<TResult, TError>.Failure(error.Error),
        _ => throw new InvalidOperationException("Unknown Result case.")
    };

    public Result<TResult, TError> Bind<TResult>(Func<TValue, Result<TResult, TError>> bind) => this switch
    {
        Ok value => bind(value.Value),
        Err error => Result<TResult, TError>.Failure(error.Error),
        _ => throw new InvalidOperationException("Unknown Result case.")
    };

    public Result<TValue, TError> Tap(Action<TValue> action)
    {
        if (this is Ok value)
        {
            action(value.Value);
        }

        return this;
    }

    public Result<TValue, TNextError> MapError<TNextError>(Func<TError, TNextError> map) => this switch
    {
        Ok value => Result<TValue, TNextError>.Success(value.Value),
        Err error => Result<TValue, TNextError>.Failure(map(error.Error)),
        _ => throw new InvalidOperationException("Unknown Result case.")
    };
}
