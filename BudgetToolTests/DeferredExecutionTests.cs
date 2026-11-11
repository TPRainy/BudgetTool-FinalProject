using Xunit;

namespace BudgetTool.Tests;

public class DeferredExecutionTests
{
    private static readonly RawTransaction?[] Raw =
    {
        new("T1", "salary", "1000", "income"),
        new("T2", "food", "-5", "expense"),
        new("T3", "rent", "300", "expense"),
    };

    private int _pulled;

    private IEnumerable<RawTransaction?> Probe()
    {
        foreach (var record in Raw)
        {
            _pulled++;
            yield return record;
        }
    }

    [Fact]
    public void Check_DoesNothingWhenCreated_AndProcessesRecordsDuringIteration()
    {
        var checks = Validation.Check(Probe());
        Assert.Equal(0, _pulled);

        var first = checks.First();
        Assert.Equal(1, _pulled);
        Assert.Equal(1, Assert.IsType<AcceptedRecord>(first).Number);

        var rejected = checks.OfType<RejectedRecord>().First();
        Assert.Equal(1 + 2, _pulled);
        Assert.Contains("negative", rejected.Message);
    }

    [Fact]
    public void RepeatedEnumeration_RunsAgain_ButASnapshotIsReused()
    {
        var checks = Validation.Check(Probe());

        Assert.Equal(3, checks.Count());
        Assert.Equal(3, checks.Count());
        Assert.Equal(6, _pulled);

        var snapshot = checks.ToList();
        Assert.Equal(9, _pulled);

        Assert.Equal(2, snapshot.OfType<AcceptedRecord>().Count());
        Assert.Single(snapshot.OfType<RejectedRecord>());
        Assert.Equal(9, _pulled);
    }

    [Fact]
    public void ParseAll_MaterialisesOnce_AndChecksArgumentsImmediately()
    {
        var outcome = Validation.ParseAll(Probe());

        Assert.Equal(3, _pulled);
        Assert.Equal(2, outcome.Transactions.Count);
        Assert.Single(outcome.Errors);

        IEnumerable<RawTransaction?>? missing = null;
#pragma warning disable CS8604
        Assert.Throws<ArgumentNullException>(() => Validation.Check(missing));
#pragma warning restore CS8604
    }
}
