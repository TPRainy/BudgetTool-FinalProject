namespace BudgetTool;

public sealed record Policy(string Name, Func<decimal, decimal> LimitFor);

public static class Policies
{
    public static IReadOnlyList<string> Known { get; } = new[] { "moderate", "strict" };

    public static Func<decimal, decimal> LimitShare(decimal share)
    {
        if (share <= 0m || share > 1m)
            throw new ArgumentOutOfRangeException(nameof(share), "share must be above 0 and at most 1");

        return income => income * share;
    }

    public static Func<decimal, decimal> Moderate { get; } = LimitShare(0.30m);
    public static Func<decimal, decimal> Strict { get; } = LimitShare(0.20m);

    public static Option<Policy> Find(string? name)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case "moderate":
                return Option.Some(new Policy("moderate", Moderate));
            case "strict":
                return Option.Some(new Policy("strict", Strict));
            default:
                return Option.None<Policy>();
        }
    }

    public static Func<decimal, decimal>? Get(string? name) => Find(name) switch
    {
        Some<Policy> found => found.Value.LimitFor,
        _ => null
    };
}
