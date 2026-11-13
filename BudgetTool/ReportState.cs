namespace BudgetTool;

public abstract record ReportState
{
    private protected ReportState() { }
}

public sealed record Draft : ReportState
{
    public ValidatedBudget Budget { get; }

    internal Draft(ValidatedBudget budget)
    {
        Budget = budget;
    }
}

public sealed record Evaluated : ReportState
{
    public ValidatedBudget Budget { get; }
    public BudgetSummary Summary { get; }

    internal Evaluated(ValidatedBudget budget, BudgetSummary summary)
    {
        Budget = budget;
        Summary = summary;
    }
}

public sealed record Confirmed : ReportState
{
    public string PolicyName { get; }
    public BudgetSummary Summary { get; }

    internal Confirmed(string policyName, BudgetSummary summary)
    {
        PolicyName = policyName;
        Summary = summary;
    }
}

public abstract record TransitionResult
{
    private protected TransitionResult() { }
}

public sealed record Moved(ReportState Next) : TransitionResult;

public sealed record Refused : TransitionResult
{
    public Refused(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    public Error Error { get; }

    public string Reason => Error.Message;
}
