namespace BudgetTool;

public abstract record Option<T>
{
    private protected Option() { }
}

public sealed record Some<T> : Option<T>
{
    public Some(T value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value), "Some must carry a value; use None for absence");

        Value = value;
    }

    public T Value { get; }
}

public sealed record None<T> : Option<T>;

public static class Option
{
    public static Option<T> Some<T>(T value) => new Some<T>(value);

    public static Option<T> None<T>() => new None<T>();
}

public static class OptionExtensions
{
    public static Result<T> ToResult<T>(this Option<T> option, Error whenMissing)
    {
        ArgumentNullException.ThrowIfNull(option);
        ArgumentNullException.ThrowIfNull(whenMissing);

        return option switch
        {
            Some<T> some => Result.Ok(some.Value),
            None<T> => Result.Fail<T>(whenMissing),
            _ => throw new InvalidOperationException("unknown option case")
        };
    }
}
