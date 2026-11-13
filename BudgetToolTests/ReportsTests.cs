using Xunit;

namespace BudgetTool.Tests;

public class ReportsTests
{
    private static readonly Transaction[] Month =
    {
        new("I1", "salary", 1500m, TransactionKind.Income),
        new("T2", "rent", 300m, TransactionKind.Expense),
        new("T3", "food", 150m, TransactionKind.Expense),
        new("T4", "food", 50m, TransactionKind.Expense),
        new("T5", "fun", 400m, TransactionKind.Expense),
    };

    [Fact]
    public void SpendingByCategory_GroupsCountsSumsAndOrders()
    {
        var rows = Reports.SpendingByCategory(Month);

        Assert.Equal(new[] { "fun", "rent", "food" }, rows.Select(r => r.Category));
        Assert.Equal(new[] { 1, 1, 2 }, rows.Select(r => r.Count));
        Assert.Equal(new[] { 400m, 300m, 200m }, rows.Select(r => r.Total));
        Assert.Equal(new[] { 44.44m, 33.33m, 22.22m }, rows.Select(r => r.SharePercent));
    }

    [Fact]
    public void SpendingByCategory_TiedTotals_AreOrderedByCategoryName()
    {
        var items = new[]
        {
            new Transaction("A", "travel", 100m, TransactionKind.Expense),
            new Transaction("B", "books", 100m, TransactionKind.Expense),
            new Transaction("C", "cafe", 100m, TransactionKind.Expense),
        };
        var sameItemsOtherOrder = new[] { items[2], items[0], items[1] };

        Assert.Equal(new[] { "books", "cafe", "travel" }, Reports.SpendingByCategory(items).Select(r => r.Category));
        Assert.Equal(new[] { "books", "cafe", "travel" }, Reports.SpendingByCategory(sameItemsOtherOrder).Select(r => r.Category));
    }

    [Fact]
    public void SpendingByCategory_EmptyInput_AndOnlyIncome_GiveNoRows()
    {
        Assert.Empty(Reports.SpendingByCategory(Array.Empty<Transaction>()));
        Assert.Empty(Reports.SpendingByCategory(new[] { new Transaction("I1", "salary", 1000m, TransactionKind.Income) }));
    }

    [Fact]
    public void LargeExpenses_FiltersOrdersAndTotals()
    {
        var report = Reports.LargeExpenses(Month, 100m);

        Assert.Equal(new[] { "T5", "T2", "T3" }, report.Rows.Select(r => r.Id));
        Assert.Equal(850m, report.Total);
        Assert.Equal(44.44m, report.Rows[0].SharePercent);
        Assert.Equal(16.67m, report.Rows[2].SharePercent);
    }

    [Fact]
    public void LargeExpenses_MinimumIsInclusive_AndTiedAmountsAreOrderedById()
    {
        var items = new[]
        {
            new Transaction("E3", "food", 150m, TransactionKind.Expense),
            new Transaction("E1", "rent", 150m, TransactionKind.Expense),
            new Transaction("E2", "fun", 149.99m, TransactionKind.Expense),
        };

        var report = Reports.LargeExpenses(items, 150m);

        Assert.Equal(new[] { "E1", "E3" }, report.Rows.Select(r => r.Id));
        Assert.Equal(300m, report.Total);
    }

    [Fact]
    public void LargeExpenses_NoMatches_AndEmptyInput_GiveEmptyReport()
    {
        var noMatches = Reports.LargeExpenses(Month, 1000m);
        Assert.Empty(noMatches.Rows);
        Assert.Equal(0m, noMatches.Total);

        var empty = Reports.LargeExpenses(Array.Empty<Transaction>(), 0m);
        Assert.Empty(empty.Rows);
        Assert.Equal(0m, empty.Total);
    }

    [Fact]
    public void ZeroAmounts_AreValidRows_WithZeroShare()
    {
        var items = new[]
        {
            new Transaction("I1", "salary", 1000m, TransactionKind.Income),
            new Transaction("E1", "gifts", 0m, TransactionKind.Expense),
        };

        var row = Assert.Single(Reports.SpendingByCategory(items));
        Assert.Equal(0m, row.Total);
        Assert.Equal(0m, row.SharePercent);
        Assert.Single(Reports.LargeExpenses(items, 0m).Rows);
        Assert.Equal(0m, Reports.SharePercent(0m, 0m));
    }

    [Fact]
    public void InvalidRecords_AndDuplicates_NeverReachTheReports()
    {
        var raw = new RawTransaction?[]
        {
            new("T1", "salary", "1500", "income"),
            new("T2", "food", "150", "expense"),
            new("T2", "food", "150", "expense"),
            new("T7", "misc", "-5", "expense"),
            new("T8", "misc", "abc", "expense"),
        };
        var parsed = Validation.ParseAll(raw);

        var result = Analysis.Run(parsed.Transactions, new[] { "moderate" }, 0m, new NativeUsage());

        var analysis = Assert.IsType<Success<AnalysisResult>>(result).Value;
        var row = Assert.Single(analysis.CategoryReports[0].Rows);
        Assert.Equal("food", row.Category);
        Assert.Equal(1, row.Count);
        Assert.Equal(new[] { "T2" }, analysis.LargeExpenses.Rows.Select(r => r.Id));
        Assert.Equal(2, parsed.Errors.Count);
    }

    [Fact]
    public void Analysis_ReusesExplicitDomainFailures()
    {
        var noIncome = new[] { new Transaction("E1", "food", 50m, TransactionKind.Expense) };

        Assert.Equal("budget.no_income",
            Assert.IsType<Failure<AnalysisResult>>(Analysis.Run(noIncome, new[] { "moderate" }, 0m, new NativeUsage())).Error.Code);
        Assert.Equal("budget.unknown_policy",
            Assert.IsType<Failure<AnalysisResult>>(Analysis.Run(Month, new[] { "lenient" }, 0m, new NativeUsage())).Error.Code);
        Assert.Equal("report.invalid_threshold",
            Assert.IsType<Failure<AnalysisResult>>(Analysis.Run(Month, new[] { "moderate" }, -1m, new NativeUsage())).Error.Code);
    }
}
