using Xunit;

namespace BudgetTool.Tests;

public class PipelineTests
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

    [Fact]
    public void Normal_Moderate_GivesDocumentedResult()
    {
        var summary = Pipeline.Run(Sample, Policies.Moderate);

        Assert.Equal(1000m, summary.Income);
        Assert.Equal(590m, summary.Expenses);
        Assert.Equal(410m, summary.Balance);
        Assert.Equal(0, summary.OverLimitCount);
        Assert.Equal(new[] { "rent", "food", "fun" }, summary.Lines.Select(l => l.Category));
    }

    [Fact]
    public void Normalize_TrimsCategory_AndDropsDuplicateId()
    {
        var kept = Pipeline.Normalize(Sample);

        Assert.Equal(5, kept.Count);
        Assert.Contains(kept, t => t.Category == "rent");
        Assert.Single(kept, t => t.Id == "T3");
    }

    [Fact]
    public void EmptyDataset_GivesAllZeros()
    {
        var summary = Pipeline.Run(Array.Empty<Transaction>(), Policies.Moderate);

        Assert.Equal(0m, summary.Income);
        Assert.Equal(0m, summary.Expenses);
        Assert.Equal(0m, summary.Balance);
        Assert.Empty(summary.Lines);
        Assert.Equal(0, summary.OverLimitCount);
    }

    [Fact]
    public void EmptyJsonArray_IsValidEmptyDataset()
    {
        var read = JsonInputParser.Read("{\"transactions\": []}");

        Assert.Null(read.FormatError);
        Assert.Empty(read.Records);
        Assert.Empty(Validation.ParseAll(read.Records).Errors);
    }

    [Fact]
    public void SpentEqualToLimit_IsNotOver()
    {
        var moderate = Pipeline.Run(Sample, Policies.Moderate);
        Assert.False(moderate.Lines.Single(l => l.Category == "rent").IsOver);

        var strict = Pipeline.Run(Sample, Policies.Strict);
        Assert.False(strict.Lines.Single(l => l.Category == "food").IsOver);
    }

    [Fact]
    public void JsonNull_AndNonObjectItem_AreTreatedAsMissing()
    {
        var read = JsonInputParser.Read("{\"transactions\":[{\"id\":\"T1\",\"category\":null,\"amount\":1,\"kind\":\"income\"}, 5]}");
        var outcome = Validation.ParseAll(read.Records);

        Assert.Empty(outcome.Transactions);
        Assert.Equal(2, outcome.Errors.Count);
        Assert.Contains("category is missing", outcome.Errors[0]);
        Assert.Contains("record is missing", outcome.Errors[1]);
    }

    [Fact]
    public void JsonNumberAndNumericString_GiveSameAmount()
    {
        var read = JsonInputParser.Read("{\"transactions\":[{\"id\":\"A\",\"category\":\"x\",\"amount\":300,\"kind\":\"expense\"},{\"id\":\"B\",\"category\":\"x\",\"amount\":\"300\",\"kind\":\"expense\"}]}");
        var outcome = Validation.ParseAll(read.Records);

        Assert.Empty(outcome.Errors);
        Assert.Equal(300m, outcome.Transactions[0].Amount);
        Assert.Equal(300m, outcome.Transactions[1].Amount);
    }

    [Fact]
    public void BrokenJson_AndMissingTransactionsKey_GiveFormatError()
    {
        Assert.NotNull(JsonInputParser.Read("{ not json").FormatError);
        Assert.NotNull(JsonInputParser.Read("{\"other\": []}").FormatError);
        Assert.NotNull(JsonInputParser.Read("[]").FormatError);
        Assert.NotNull(JsonInputParser.Read("{\"transactions\": {}}").FormatError);
        Assert.NotNull(JsonInputParser.Read("").FormatError);
    }

    [Fact]
    public void Evaluate_InvokesDelegate()
    {
        var calls = 0;
        decimal Counting(decimal income)
        {
            calls++;
            return 100m;
        }

        var evaluation = Pipeline.Evaluate(Pipeline.Normalize(Sample), Counting);

        Assert.Equal(1, calls);
        Assert.All(evaluation.Lines, l => Assert.Equal(100m, l.Limit));
    }

    [Fact]
    public void Normalize_DoesNotModifyCallerList()
    {
        var input = new List<Transaction>(Sample);
        var snapshot = input.ToList();

        var kept = Pipeline.Normalize(input);

        Assert.Equal(snapshot, input);
        Assert.Equal(6, input.Count);
        Assert.Equal(" Rent ", input[1].Category);

        input.Add(new Transaction("T99", "extra", 1m, TransactionKind.Expense));
        Assert.Equal(5, kept.Count);
    }

    [Fact]
    public void Run_IsDeterministic()
    {
        var first = Pipeline.Run(Sample, Policies.Strict);
        var second = Pipeline.Run(Sample, Policies.Strict);

        Assert.Equal(first.Balance, second.Balance);
        Assert.Equal(first.OverLimitCount, second.OverLimitCount);
        Assert.Equal(first.Lines, second.Lines);
    }
}