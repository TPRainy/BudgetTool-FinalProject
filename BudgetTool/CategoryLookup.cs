namespace BudgetTool;

public static class CategoryLookup
{
    public static Option<CategoryLine> Find(BudgetSummary summary, string category)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(category);

        var key = category.Trim().ToLowerInvariant();
        foreach (var line in summary.Lines)
        {
            if (line.Category == key)
                return Option.Some(line);
        }

        return Option.None<CategoryLine>();
    }

    public static Result<CategoryLine> Require(BudgetSummary summary, string category)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(category);

        return Find(summary, category)
            .ToResult(DomainErrors.CategoryNotFound(category, summary.Lines.Select(l => l.Category)));
    }
}
