using Xunit;

namespace BudgetTool.Tests;

public class ExtensionAndCurryingTests
{
    private static readonly Transaction[] Month =
    {
        new("I1", "salary", 1500m, TransactionKind.Income),
        new("T2", "rent", 300m, TransactionKind.Expense),
        new("T3", "food", 200m, TransactionKind.Expense),
        new("T5", "fun", 400m, TransactionKind.Expense),
    };

    [Fact]
    public void ToCents_ConvertsMoneyToWholeCents_RoundingHalfAwayFromZero()
    {
        Assert.Equal(45000L, 450m.ToCents());
        Assert.Equal(0L, 0m.ToCents());
        Assert.Equal(1L, 0.005m.ToCents());
        Assert.Equal(0L, 0.004m.ToCents());
        Assert.Equal(12346L, 123.455m.ToCents());
        Assert.Equal(-1L, (-0.005m).ToCents());
    }

    [Fact]
    public void Sequence_AllSuccesses_GivesValuesInOrder()
    {
        var results = new[] { Result.Ok(1), Result.Ok(2), Result.Ok(3) };

        var sequenced = results.Sequence();

        Assert.Equal(new[] { 1, 2, 3 }, Assert.IsType<Success<IReadOnlyList<int>>>(sequenced).Value);
        Assert.Empty(Assert.IsType<Success<IReadOnlyList<int>>>(Array.Empty<Result<int>>().Sequence()).Value);
    }

    [Fact]
    public void Sequence_StopsAtFirstFailure_AndDoesNotProduceTheRest()
    {
        var produced = 0;
        var error = new Error("test.failed", "the second item failed", ErrorKind.Domain);

        var results = new[] { 1, 2, 3 }.Select(n =>
        {
            produced++;
            return n == 2 ? Result.Fail<int>(error) : Result.Ok(n);
        });

        var sequenced = results.Sequence();

        Assert.Same(error, Assert.IsType<Failure<IReadOnlyList<int>>>(sequenced).Error);
        Assert.Equal(2, produced);
    }

    [Fact]
    public void LimitShare_IsCurried_AndPartialApplicationsGiveDifferentLimits()
    {
        Assert.Equal(300m, Policies.LimitShare(0.30m)(1000m));

        Func<decimal, decimal> moderate = Policies.Moderate;
        Func<decimal, decimal> strict = Policies.Strict;

        Assert.Equal(300m, moderate(1000m));
        Assert.Equal(200m, strict(1000m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Policies.LimitShare(0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Policies.LimitShare(1.5m));
    }

    [Fact]
    public void ConfiguredLimits_GiveDifferentResultsOnTheSameData()
    {
        Assert.Equal(0, Pipeline.Run(Month, Policies.Moderate).OverLimitCount);
        Assert.Equal(1, Pipeline.Run(Month, Policies.Strict).OverLimitCount);

        var result = Analysis.Run(Month, new[] { "moderate", "strict" }, 100m, new NativeUsage());

        var reports = Assert.IsType<Success<AnalysisResult>>(result).Value.CategoryReports;
        Assert.Equal(450m, reports[0].Limit);
        Assert.Equal(300m, reports[1].Limit);
        Assert.Equal("fun", reports[0].Rows[0].Category);
        Assert.Equal(89L, reports[0].Rows[0].UsagePercent);
        Assert.Equal(133L, reports[1].Rows[0].UsagePercent);
    }
}
