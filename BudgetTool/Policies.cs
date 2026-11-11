namespace BudgetTool;

public sealed record Policy(string Name, Func<decimal, decimal> LimitFor);

public static class Policies
{
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
}
