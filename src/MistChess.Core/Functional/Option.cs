namespace MistChess.Core.Functional;

public abstract record Option<T>
{
    protected Option() { }

    public sealed record Some(T Value) : Option<T>;
    public sealed record NoneOption : Option<T>;

    public static Option<T> None { get; } = new NoneOption();
    public static Option<T> SomeOf(T value) => new Some(value);

    public bool IsSome => this is Some;
    public bool IsNone => this is NoneOption;

    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) => this switch
    {
        Some value => some(value.Value),
        NoneOption => none(),
        _ => throw new InvalidOperationException("Unknown Option case.")
    };

    public Option<TResult> Map<TResult>(Func<T, TResult> map) => this switch
    {
        Some value => Option<TResult>.SomeOf(map(value.Value)),
        NoneOption => Option<TResult>.None,
        _ => throw new InvalidOperationException("Unknown Option case.")
    };

    public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> bind) => this switch
    {
        Some value => bind(value.Value),
        NoneOption => Option<TResult>.None,
        _ => throw new InvalidOperationException("Unknown Option case.")
    };

    public Option<T> Tap(Action<T> action)
    {
        if (this is Some value)
        {
            action(value.Value);
        }

        return this;
    }
}
