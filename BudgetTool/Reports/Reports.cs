namespace BudgetTool;

public sealed record SpendingRow(string Category, int Count, decimal Total, decimal SharePercent);

public sealed record LargeExpenseRow(string Id, string Category, decimal Amount, decimal SharePercent);

public sealed record LargeExpenseReport(decimal MinAmount, IReadOnlyList<LargeExpenseRow> Rows, decimal Total);

public static class Reports
{
    public static IReadOnlyList<SpendingRow> SpendingByCategory(IEnumerable<Transaction> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var expenses = items.Where(t => t.Kind == TransactionKind.Expense).ToList();
        var allExpenses = expenses.Sum(t => t.Amount);

        return expenses
            .GroupBy(t => t.Category)
            .Select(group =>
            {
                var total = group.Sum(t => t.Amount);
                return new SpendingRow(group.Key, group.Count(), total, SharePercent(total, allExpenses));
            })
            .OrderByDescending(row => row.Total)
            .ThenBy(row => row.Category, StringComparer.Ordinal)
            .ToList();
    }

    public static LargeExpenseReport LargeExpenses(IEnumerable<Transaction> items, decimal minAmount)
    {
        ArgumentNullException.ThrowIfNull(items);

        var expenses = items.Where(t => t.Kind == TransactionKind.Expense).ToList();
        var allExpenses = expenses.Sum(t => t.Amount);

        var rows = expenses
            .Where(t => t.Amount >= minAmount)
            .OrderByDescending(t => t.Amount)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .Select(t => new LargeExpenseRow(t.Id, t.Category, t.Amount, SharePercent(t.Amount, allExpenses)))
            .ToList();

        return new LargeExpenseReport(minAmount, rows, rows.Sum(r => r.Amount));
    }

    public static decimal SharePercent(decimal part, decimal whole) =>
        whole == 0m ? 0m : decimal.Round(part / whole * 100m, 2, MidpointRounding.AwayFromZero);
}
