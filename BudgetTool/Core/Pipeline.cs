namespace BudgetTool;

public static class Pipeline
{
    public static BudgetSummary Run(IReadOnlyList<Transaction> input, Func<decimal, decimal> limitFor)
    {
        return BuildSummary(Evaluate(Normalize(input), limitFor));
    }

    public static IReadOnlyList<Transaction> Normalize(IEnumerable<Transaction> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input
            .Select(t => t with { Category = t.Category.Trim().ToLowerInvariant() })
            .GroupBy(t => t.Id.ToLowerInvariant())
            .Select(group => group.First())
            .ToList();
    }

    public static Evaluation Evaluate(IReadOnlyList<Transaction> items, Func<decimal, decimal> limitFor)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(limitFor);

        var income = items.Where(t => t.Kind == TransactionKind.Income).Sum(t => t.Amount);
        var expenses = items.Where(t => t.Kind == TransactionKind.Expense).Sum(t => t.Amount);
        var limit = limitFor(income);

        var lines = items
            .Where(t => t.Kind == TransactionKind.Expense)
            .GroupBy(t => t.Category)
            .Select(group =>
            {
                var spent = group.Sum(t => t.Amount);
                return new CategoryLine(group.Key, spent, limit, spent > limit);
            })
            .ToList();

        return new Evaluation(income, expenses, lines);
    }

    public static BudgetSummary BuildSummary(Evaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        var ordered = evaluation.Lines
            .OrderByDescending(l => l.Spent)
            .ThenBy(l => l.Category, StringComparer.Ordinal)
            .ToList();

        return new BudgetSummary(
            evaluation.Income,
            evaluation.Expenses,
            evaluation.Income - evaluation.Expenses,
            ordered,
            ordered.Count(l => l.IsOver));
    }
}