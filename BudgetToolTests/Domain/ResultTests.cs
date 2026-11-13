using Xunit;

namespace BudgetTool.Tests;

public class ResultTests
{
    private static readonly Error SampleError = new("test.failed", "the first step failed", ErrorKind.Domain);

    [Fact]
    public void Map_Success_PassesValue_AndReturnsNewValue()
    {
        var received = 0;

        var mapped = Result.Ok(21).Map(x =>
        {
            received = x;
            return x * 2;
        });

        Assert.Equal(21, received);
        Assert.Equal(42, Assert.IsType<Success<int>>(mapped).Value);
    }

    [Fact]
    public void Bind_Success_PassesValue_AndKeepsTheNextResult()
    {
        var received = 0;

        var bound = Result.Ok(5).Bind(x =>
        {
            received = x;
            return Result.Ok("value " + x);
        });

        Assert.Equal(5, received);
        Assert.Equal("value 5", Assert.IsType<Success<string>>(bound).Value);

        var nextFailure = Result.Fail<string>(SampleError);
        var boundToFailure = Result.Ok(5).Bind(x => nextFailure);

        Assert.Same(nextFailure, boundToFailure);
    }

    [Fact]
    public void MapAndBind_OnFailure_DoNotCallTheFunction_AndKeepTheError()
    {
        var calls = 0;
        var failed = Result.Fail<int>(SampleError);

        var mapped = failed.Map(x =>
        {
            calls++;
            return x + 1;
        });
        var bound = failed.Bind(x =>
        {
            calls++;
            return Result.Ok(x + 1);
        });

        Assert.Equal(0, calls);
        Assert.Same(SampleError, Assert.IsType<Failure<int>>(mapped).Error);
        Assert.Same(SampleError, Assert.IsType<Failure<int>>(bound).Error);
    }

    [Fact]
    public void ProjectChain_AfterFactoryFailure_SkipsEveryLaterStep()
    {
        var calls = 0;
        var noIncome = new[] { new Transaction("E1", "food", 50m, TransactionKind.Expense) };

        var result = ReportFlow.CreateBudget(noIncome, "moderate")
            .Map(budget =>
            {
                calls++;
                return Workflow.Start(budget);
            })
            .Bind(draft =>
            {
                calls++;
                return ReportFlow.Evaluate(draft);
            });

        Assert.Equal(0, calls);
        Assert.Equal("budget.no_income", Assert.IsType<Failure<Evaluated>>(result).Error.Code);
    }

    [Fact]
    public void Constructors_RejectContradictoryStates()
    {
        string? missingValue = null;
        Error? missingError = null;

#pragma warning disable CS8604
        Assert.Throws<ArgumentNullException>(() => new Success<string>(missingValue));
        Assert.Throws<ArgumentNullException>(() => new Failure<string>(missingError));
        Assert.Throws<ArgumentNullException>(() => Option.Some<string>(missingValue));
#pragma warning restore CS8604

        Assert.Throws<ArgumentException>(() => new Error("", "message", ErrorKind.Domain));
        Assert.Throws<ArgumentException>(() => new Error("code", " ", ErrorKind.Domain));
        Assert.Throws<ArgumentException>(() => new BudgetInvalid(Array.Empty<Error>()));
    }

    [Fact]
    public void Option_ZeroFalseAndEmptyString_ArePresentValues()
    {
        Assert.Equal(0m, Assert.IsType<Some<decimal>>(Option.Some(0m)).Value);
        Assert.False(Assert.IsType<Some<bool>>(Option.Some(false)).Value);
        Assert.Equal("", Assert.IsType<Some<string>>(Option.Some("")).Value);

        Assert.Equal(0m, Assert.IsType<Success<decimal>>(Option.Some(0m).ToResult(SampleError)).Value);

        var missing = Option.None<decimal>().ToResult(SampleError);
        Assert.Same(SampleError, Assert.IsType<Failure<decimal>>(missing).Error);
    }
}
