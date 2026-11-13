namespace BudgetTool;

public static class Workflow
{
    public static ReportState Start(ValidatedBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return new Draft(budget);
    }

    public static TransitionResult Evaluate(ReportState? state) => state switch
    {
        Draft d => new Moved(new Evaluated(d.Budget, Pipeline.Run(d.Budget.Transactions, d.Budget.LimitFor))),
        Evaluated => new Refused(DomainErrors.AlreadyEvaluated()),
        Confirmed => new Refused(DomainErrors.AlreadyConfirmed()),
        _ => new Refused(DomainErrors.UnknownState())
    };

    public static TransitionResult Confirm(ReportState? state) => state switch
    {
        Evaluated e when e.Summary.OverLimitCount == 0 => new Moved(new Confirmed(e.Budget.PolicyName, e.Summary)),
        Evaluated e => new Refused(DomainErrors.OverLimit(e.Summary.Lines.Where(l => l.IsOver))),
        Confirmed c => new Moved(c),
        Draft => new Refused(DomainErrors.NotEvaluated()),
        _ => new Refused(DomainErrors.UnknownState())
    };
}
