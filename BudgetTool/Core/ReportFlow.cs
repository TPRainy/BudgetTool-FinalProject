namespace BudgetTool;

public sealed record ConfirmedReport(Confirmed State, Option<CategoryLine> Focus);

public sealed record ReportView(
    string PolicyName,
    decimal Balance,
    int OverLimitCount,
    Option<CategoryLine> Focus,
    IReadOnlyList<string> Lines)
{
    public static ReportView From(ConfirmedReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var state = report.State;
        var lines = Display.Format(state.PolicyName, state.Summary).Split('\n').ToList();

        if (report.Focus is Some<CategoryLine> focus)
            lines.Add("  Focus category: " + Display.DescribeLine(focus.Value));

        lines.Add("  Status: " + Display.DescribeState(state));

        return new ReportView(state.PolicyName, state.Summary.Balance, state.Summary.OverLimitCount, report.Focus, lines);
    }
}

public static class ReportFlow
{
    public static Result<ReportView> Run(IEnumerable<Transaction> items, string? policyName, Option<string> category) =>
        CreateBudget(items, policyName)
            .Map(budget => Workflow.Start(budget))
            .Bind(draft => Evaluate(draft))
            .Bind(evaluated => Confirm(evaluated))
            .Bind(confirmed => Focus(confirmed, category))
            .Map(report => ReportView.From(report));

    public static Result<ReportView> ConfirmWithoutEvaluation(IEnumerable<Transaction> items, string? policyName) =>
        CreateBudget(items, policyName)
            .Map(budget => Workflow.Start(budget))
            .Bind(draft => Confirm(draft))
            .Bind(confirmed => Focus(confirmed, Option.None<string>()))
            .Map(report => ReportView.From(report));

    public static Result<ValidatedBudget> CreateBudget(IEnumerable<Transaction> items, string? policyName) =>
        ValidatedBudget.Create(items, policyName) switch
        {
            BudgetValid valid => Result.Ok(valid.Budget),
            BudgetInvalid invalid when invalid.Errors.Count == 1 => Result.Fail<ValidatedBudget>(invalid.Errors[0]),
            BudgetInvalid invalid => Result.Fail<ValidatedBudget>(DomainErrors.InvalidBudget(invalid.Errors)),
            _ => throw new InvalidOperationException("unknown budget creation case")
        };

    public static Result<Evaluated> Evaluate(ReportState state) => Workflow.Evaluate(state) switch
    {
        Moved { Next: Evaluated evaluated } => Result.Ok(evaluated),
        Refused refused => Result.Fail<Evaluated>(refused.Error),
        _ => Result.Fail<Evaluated>(DomainErrors.UnknownState())
    };

    public static Result<Confirmed> Confirm(ReportState state) => Workflow.Confirm(state) switch
    {
        Moved { Next: Confirmed confirmed } => Result.Ok(confirmed),
        Refused refused => Result.Fail<Confirmed>(refused.Error),
        _ => Result.Fail<Confirmed>(DomainErrors.UnknownState())
    };

    public static Result<ConfirmedReport> Focus(Confirmed confirmed, Option<string> category)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        ArgumentNullException.ThrowIfNull(category);

        return category switch
        {
            Some<string> wanted => CategoryLookup.Require(confirmed.Summary, wanted.Value)
                .Map(line => new ConfirmedReport(confirmed, Option.Some(line))),
            None<string> => Result.Ok(new ConfirmedReport(confirmed, Option.None<CategoryLine>())),
            _ => throw new InvalidOperationException("unknown option case")
        };
    }
}
