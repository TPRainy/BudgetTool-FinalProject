namespace BudgetTool;

public abstract record Result<T>
{
    private protected Result() { }
}

public sealed record Success<T> : Result<T>
{
    public Success(T value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value), "a success must carry a value");

        Value = value;
    }

    public T Value { get; }
}

public sealed record Failure<T> : Result<T>
{
    public Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    public Error Error { get; }
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => new Success<T>(value);

    public static Result<T> Fail<T>(Error error) => new Failure<T>(error);
}

public static class ResultExtensions
{
    public static Result<U> Map<T, U>(this Result<T> result, Func<T, U> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);

        return result switch
        {
            Success<T> success => Result.Ok(map(success.Value)),
            Failure<T> failure => Result.Fail<U>(failure.Error),
            _ => throw new InvalidOperationException("unknown result case")
        };
    }

    public static Result<U> Bind<T, U>(this Result<T> result, Func<T, Result<U>> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);

        return result switch
        {
            Success<T> success => bind(success.Value)
                ?? throw new InvalidOperationException("the next operation returned no result"),
            Failure<T> failure => Result.Fail<U>(failure.Error),
            _ => throw new InvalidOperationException("unknown result case")
        };
    }

    public static Result<IReadOnlyList<T>> Sequence<T>(this IEnumerable<Result<T>> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var values = new List<T>();
        foreach (var result in results)
        {
            switch (result)
            {
                case Success<T> success:
                    values.Add(success.Value);
                    break;
                case Failure<T> failure:
                    return Result.Fail<IReadOnlyList<T>>(failure.Error);
                default:
                    throw new InvalidOperationException("unknown result case");
            }
        }

        return Result.Ok<IReadOnlyList<T>>(values.AsReadOnly());
    }
}
