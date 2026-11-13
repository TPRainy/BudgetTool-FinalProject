using Xunit;

namespace BudgetTool.Tests;

public class ReportFlowTests
{
    private static readonly Transaction[] Sample =
    {
        new("T1", "salary", 1000m, TransactionKind.Income),
        new("T2", " Rent ", 300m, TransactionKind.Expense),
        new("T3", "food", 150m, TransactionKind.Expense),
        new("T4", "food", 50m, TransactionKind.Expense),
        new("T5", "fun", 90m, TransactionKind.Expense),
        new("T3", "food", 150m, TransactionKind.Expense),
    };

    private static readonly Transaction[] NoIncome =
    {
        new("E1", "food", 50m, TransactionKind.Expense),
    };

    private static Error FailureOf<T>(Result<T> result) => Assert.IsType<Failure<T>>(result).Error;

    [Fact]
    public void CompleteSuccess_Moderate_WithFocusCategory()
    {
        var result = ReportFlow.Run(Sample, "moderate", Option.Some(" Food "));

        var view = Assert.IsType<Success<ReportView>>(result).Value;
        Assert.Equal("moderate", view.PolicyName);
        Assert.Equal(410m, view.Balance);
        Assert.Equal(0, view.OverLimitCount);

        var focus = Assert.IsType<Some<CategoryLine>>(view.Focus).Value;
        Assert.Equal("food", focus.Category);
        Assert.Equal(200m, focus.Spent);
        Assert.Equal(300m, focus.Limit);
        Assert.False(focus.IsOver);

        Assert.Contains(view.Lines, l => l.Contains("Focus category: food"));
        Assert.Contains(view.Lines, l => l.Contains("Status: Confirmed"));
    }

    [Fact]
    public void OptionalCategory_NotGiven_ReportHasNoFocus()
    {
        var result = ReportFlow.Run(Sample, "moderate", Option.None<string>());

        var view = Assert.IsType<Success<ReportView>>(result).Value;
        Assert.IsType<None<CategoryLine>>(view.Focus);
        Assert.DoesNotContain(view.Lines, l => l.Contains("Focus category"));
    }

    [Fact]
    public void DomainFailure_NoIncome_IsReportedWithCode()
    {
        var error = FailureOf(ReportFlow.Run(NoIncome, "moderate", Option.None<string>()));

        Assert.Equal("budget.no_income", error.Code);
        Assert.Equal(ErrorKind.Domain, error.Kind);
        Assert.Contains("income", error.Message);
    }

    [Fact]
    public void DomainFailure_UnknownPolicy_NamesThePolicy()
    {
        var error = FailureOf(ReportFlow.Run(Sample, "lenient", Option.None<string>()));

        Assert.Equal("budget.unknown_policy", error.Code);
        Assert.Contains("lenient", error.Message);
    }

    [Fact]
    public void DomainFailure_OverLimit_NamesTheCategory()
    {
        var error = FailureOf(ReportFlow.Run(Sample, "strict", Option.None<string>()));

        Assert.Equal("report.over_limit", error.Code);
        Assert.Contains("rent", error.Message);
        Assert.DoesNotContain("food", error.Message);
    }

    [Fact]
    public void DomainFailure_ConfirmBeforeEvaluate_IsRejectedByState()
    {
        var error = FailureOf(ReportFlow.ConfirmWithoutEvaluation(Sample, "moderate"));

        Assert.Equal("report.not_evaluated", error.Code);
        Assert.Contains("not been evaluated", error.Message);
    }

    [Fact]
    public void DomainFailure_MissingRequiredCategory_NamesItAndListsAvailable()
    {
        var error = FailureOf(ReportFlow.Run(Sample, "moderate", Option.Some("travel")));

        Assert.Equal("lookup.category_not_found", error.Code);
        Assert.Contains("travel", error.Message);
        Assert.Contains("rent", error.Message);
    }

    [Fact]
    public void DomainFailure_SeveralBrokenRules_AreReportedTogether()
    {
        var error = FailureOf(ReportFlow.Run(NoIncome, "lenient", Option.None<string>()));

        Assert.Equal("budget.invalid", error.Code);
        Assert.Contains("budget.no_income", error.Message);
        Assert.Contains("budget.unknown_policy", error.Message);
    }

    [Fact]
    public void Lookup_PresentCategory_IsFound_IgnoringCaseAndSpaces()
    {
        var summary = Pipeline.Run(Sample, Policies.Moderate);

        var found = Assert.IsType<Some<CategoryLine>>(CategoryLookup.Find(summary, " RENT "));

        Assert.Equal("rent", found.Value.Category);
        Assert.Equal(300m, found.Value.Spent);
    }

    [Fact]
    public void Lookup_ZeroSpentCategory_IsPresent_NotAbsent()
    {
        var items = new[]
        {
            new Transaction("I1", "salary", 1000m, TransactionKind.Income),
            new Transaction("E1", "gifts", 0m, TransactionKind.Expense),
        };
        var summary = Pipeline.Run(items, Policies.Moderate);

        var found = Assert.IsType<Some<CategoryLine>>(CategoryLookup.Find(summary, "gifts"));
        Assert.Equal(0m, found.Value.Spent);

        var required = Assert.IsType<Success<CategoryLine>>(CategoryLookup.Require(summary, "gifts"));
        Assert.Equal(0m, required.Value.Spent);
    }

    [Fact]
    public void Lookup_UnknownCategory_IsNone_AndRequiredLookupFails()
    {
        var summary = Pipeline.Run(Sample, Policies.Moderate);

        Assert.IsType<None<CategoryLine>>(CategoryLookup.Find(summary, "travel"));
        Assert.IsType<None<CategoryLine>>(CategoryLookup.Find(summary, ""));

        var error = FailureOf(CategoryLookup.Require(summary, "travel"));
        Assert.Equal("lookup.category_not_found", error.Code);
    }
}
