using Xunit;

namespace BudgetTool.Tests;

public class WorkflowTests
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

    private static ValidatedBudget CreateBudget(IEnumerable<Transaction> items, string policy)
    {
        var creation = ValidatedBudget.Create(items, policy);
        return Assert.IsType<BudgetValid>(creation).Budget;
    }

    private static Evaluated EvaluateDraft(ValidatedBudget budget)
    {
        var moved = Assert.IsType<Moved>(Workflow.Evaluate(Workflow.Start(budget)));
        return Assert.IsType<Evaluated>(moved.Next);
    }

    private static TransitionResult ConfirmWithExpense(decimal expense)
    {
        var items = new[]
        {
            new Transaction("I1", "salary", 1000m, TransactionKind.Income),
            new Transaction("E1", "food", expense, TransactionKind.Expense),
        };
        var budget = CreateBudget(items, "strict");
        return Workflow.Confirm(EvaluateDraft(budget));
    }

    [Fact]
    public void Factory_ValidInput_CreatesUsableBudget()
    {
        var budget = CreateBudget(Sample, " Moderate ");

        Assert.Equal("moderate", budget.PolicyName);
        Assert.Equal(6, budget.Transactions.Count);
        Assert.Equal(300m, budget.LimitFor(1000m));
        Assert.False(budget.Transactions is List<Transaction>);
    }

    [Fact]
    public void Factory_NoIncome_IsRejected_WithReason()
    {
        var items = new[] { new Transaction("E1", "food", 50m, TransactionKind.Expense) };

        var rejected = Assert.IsType<BudgetInvalid>(ValidatedBudget.Create(items, "moderate"));

        var reason = Assert.Single(rejected.Reasons);
        Assert.Contains("income", reason);
    }

    [Fact]
    public void Factory_IncomeBoundary_ZeroRejected_SmallestPositiveAccepted()
    {
        var zero = new[] { new Transaction("I1", "salary", 0m, TransactionKind.Income) };
        var tiny = new[] { new Transaction("I1", "salary", 0.01m, TransactionKind.Income) };

        Assert.IsType<BudgetInvalid>(ValidatedBudget.Create(zero, "moderate"));
        Assert.IsType<BudgetValid>(ValidatedBudget.Create(tiny, "moderate"));
    }

    [Fact]
    public void Factory_UnknownOrMissingPolicy_IsRejected_WithReason()
    {
        var unknown = Assert.IsType<BudgetInvalid>(ValidatedBudget.Create(Sample, "lenient"));
        Assert.Contains(unknown.Reasons, r => r.Contains("lenient"));

        var missing = Assert.IsType<BudgetInvalid>(ValidatedBudget.Create(Sample, null));
        Assert.Contains(missing.Reasons, r => r.Contains("unknown policy"));
    }

    [Fact]
    public void Factory_ReportsEveryBrokenRule()
    {
        var items = new[] { new Transaction("E1", "food", 50m, TransactionKind.Expense) };

        var rejected = Assert.IsType<BudgetInvalid>(ValidatedBudget.Create(items, "lenient"));

        Assert.Equal(2, rejected.Reasons.Count);
    }

    [Fact]
    public void Factory_NullItems_Throws()
    {
        IEnumerable<Transaction>? missing = null;

#pragma warning disable CS8604
        Assert.Throws<ArgumentNullException>(() => ValidatedBudget.Create(missing, "moderate"));
#pragma warning restore CS8604
    }

    [Fact]
    public void DistinctCases_AreReachable_AndMatchedExplicitly()
    {
        Assert.IsType<BudgetValid>(ValidatedBudget.Create(Sample, "moderate"));
        Assert.IsType<BudgetInvalid>(ValidatedBudget.Create(Sample, "lenient"));

        var budget = CreateBudget(Sample, "moderate");
        var draft = Workflow.Start(budget);
        var evaluated = EvaluateDraft(budget);
        var moved = Workflow.Confirm(evaluated);
        var confirmed = Assert.IsType<Confirmed>(Assert.IsType<Moved>(moved).Next);
        var refused = Workflow.Confirm(draft);

        Assert.StartsWith("Draft", Display.DescribeState(draft));
        Assert.StartsWith("Evaluated", Display.DescribeState(evaluated));
        Assert.StartsWith("Confirmed", Display.DescribeState(confirmed));
        Assert.StartsWith("OK", Display.DescribeResult(moved));
        Assert.StartsWith("REFUSED", Display.DescribeResult(refused));
    }

    [Fact]
    public void Factory_CopiesInput_AndFailureLeavesCallerDataUnchanged()
    {
        var source = new List<Transaction>(Sample);
        var budget = CreateBudget(source, "moderate");

        source.Add(new Transaction("T99", "extra", 1m, TransactionKind.Expense));
        Assert.Equal(6, budget.Transactions.Count);

        var bad = new List<Transaction> { new("E1", "food", 10m, TransactionKind.Expense) };
        var snapshot = bad.ToList();

        Assert.IsType<BudgetInvalid>(ValidatedBudget.Create(bad, "moderate"));
        Assert.Equal(snapshot, bad);
    }

    [Fact]
    public void NormalOperation_Moderate_EvaluateThenConfirm()
    {
        var evaluated = EvaluateDraft(CreateBudget(Sample, "moderate"));

        Assert.Equal(1000m, evaluated.Summary.Income);
        Assert.Equal(590m, evaluated.Summary.Expenses);
        Assert.Equal(410m, evaluated.Summary.Balance);
        Assert.Equal(0, evaluated.Summary.OverLimitCount);

        var moved = Assert.IsType<Moved>(Workflow.Confirm(evaluated));
        var confirmed = Assert.IsType<Confirmed>(moved.Next);
        Assert.Same(evaluated.Summary, confirmed.Summary);
    }

    [Fact]
    public void MissingPrerequisite_ConfirmBeforeEvaluate_IsRefused()
    {
        var draft = Workflow.Start(CreateBudget(Sample, "moderate"));

        var refused = Assert.IsType<Refused>(Workflow.Confirm(draft));

        Assert.Contains("evaluated", refused.Reason);
    }

    [Fact]
    public void OverLimit_Strict_ConfirmIsRefused()
    {
        var evaluated = EvaluateDraft(CreateBudget(Sample, "strict"));

        Assert.Equal(1, evaluated.Summary.OverLimitCount);

        var refused = Assert.IsType<Refused>(Workflow.Confirm(evaluated));
        Assert.Contains("over limit", refused.Reason);
    }

    [Fact]
    public void Repeated_Confirm_ReturnsTheSameConfirmedState()
    {
        var evaluated = EvaluateDraft(CreateBudget(Sample, "moderate"));
        var first = Assert.IsType<Moved>(Workflow.Confirm(evaluated));
        var second = Assert.IsType<Moved>(Workflow.Confirm(first.Next));

        Assert.IsType<Confirmed>(second.Next);
        Assert.Same(first.Next, second.Next);
    }

    [Fact]
    public void Repeated_Evaluate_IsRefused()
    {
        var evaluated = EvaluateDraft(CreateBudget(Sample, "moderate"));
        var confirmed = Assert.IsType<Moved>(Workflow.Confirm(evaluated)).Next;

        Assert.IsType<Refused>(Workflow.Evaluate(evaluated));
        Assert.IsType<Refused>(Workflow.Evaluate(confirmed));
    }

    [Fact]
    public void Boundary_ExpenseEqualToLimit_CanBeConfirmed()
    {
        Assert.IsType<Moved>(ConfirmWithExpense(200m));
        Assert.IsType<Refused>(ConfirmWithExpense(200.01m));
    }

    [Fact]
    public void StatePreservation_FailedTransition_LeavesStatesUnchanged()
    {
        var budget = CreateBudget(Sample, "strict");
        var draft = Assert.IsType<Draft>(Workflow.Start(budget));

        Assert.IsType<Refused>(Workflow.Confirm(draft));
        Assert.Equal(6, draft.Budget.Transactions.Count);

        var evaluated = Assert.IsType<Evaluated>(Assert.IsType<Moved>(Workflow.Evaluate(draft)).Next);
        Assert.IsType<Refused>(Workflow.Confirm(evaluated));
        Assert.Equal(1, evaluated.Summary.OverLimitCount);
        Assert.Equal(410m, evaluated.Summary.Balance);
        Assert.Equal(6, draft.Budget.Transactions.Count);
    }

    [Fact]
    public void NullState_IsRefused_NotTreatedAsValid()
    {
        Assert.IsType<Refused>(Workflow.Evaluate(null));
        Assert.IsType<Refused>(Workflow.Confirm(null));
    }
}
