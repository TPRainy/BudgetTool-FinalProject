namespace BudgetTool;

public sealed record CategoryUsageRow(string Category, int Count, decimal Total, decimal SharePercent, long UsagePercent);

public sealed record CategoryReport(
    string PolicyName,
    decimal Income,
    decimal Limit,
    decimal TotalExpenses,
    IReadOnlyList<CategoryUsageRow> Rows);

public sealed record AnalysisResult(IReadOnlyList<CategoryReport> CategoryReports, LargeExpenseReport LargeExpenses);

public static class Analysis
{
    public static Result<AnalysisResult> Run(
        IEnumerable<Transaction> items,
        IEnumerable<string> policyNames,
        decimal minAmount,
        NativeUsage usage)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(policyNames);
        ArgumentNullException.ThrowIfNull(usage);

        if (minAmount < 0m)
            return Result.Fail<AnalysisResult>(DomainErrors.InvalidThreshold(minAmount));

        var normalized = Pipeline.Normalize(items);

        return policyNames
            .Select(name => BuildCategoryReport(normalized, name, usage))
            .Sequence()
            .Map(reports => new AnalysisResult(reports, Reports.LargeExpenses(normalized, minAmount)));
    }

    public static Result<CategoryReport> BuildCategoryReport(IEnumerable<Transaction> items, string? policyName, NativeUsage usage) =>
        ReportFlow.CreateBudget(items, policyName)
            .Bind(budget => AddUsage(budget, usage));

    private static Result<CategoryReport> AddUsage(ValidatedBudget budget, NativeUsage usage)
    {
        var income = budget.Transactions.Where(t => t.Kind == TransactionKind.Income).Sum(t => t.Amount);
        var limit = budget.LimitFor(income);
        var rows = Reports.SpendingByCategory(budget.Transactions);

        return rows
            .Select(row => usage.Percent(row.Total, limit)
                .Map(percent => new CategoryUsageRow(row.Category, row.Count, row.Total, row.SharePercent, percent)))
            .Sequence()
            .Map(usageRows => new CategoryReport(budget.PolicyName, income, limit, rows.Sum(r => r.Total), usageRows));
    }
}
