namespace BudgetTool;

public abstract record BudgetCreation
{
    private protected BudgetCreation() { }
}

public sealed record BudgetValid(ValidatedBudget Budget) : BudgetCreation;

public sealed record BudgetInvalid(IReadOnlyList<string> Reasons) : BudgetCreation;

public sealed class ValidatedBudget
{
    public IReadOnlyList<Transaction> Transactions { get; }
    public string PolicyName { get; }
    public Func<decimal, decimal> LimitFor { get; }

    private ValidatedBudget(IReadOnlyList<Transaction> transactions, string policyName, Func<decimal, decimal> limitFor)
    {
        Transactions = transactions;
        PolicyName = policyName;
        LimitFor = limitFor;
    }

    public static BudgetCreation Create(IEnumerable<Transaction> items, string? policyName)
    {
        ArgumentNullException.ThrowIfNull(items);

        var list = items.ToList();
        var reasons = new List<string>();

        if (!list.Any(t => t.Kind == TransactionKind.Income && t.Amount > 0))
            reasons.Add("at least one income transaction with an amount above 0 is required");

        var name = policyName?.Trim().ToLowerInvariant();
        var limitFor = Policies.Get(name);
        if (limitFor is null || name is null)
            reasons.Add("unknown policy '" + (policyName ?? "(none)") + "'");

        if (reasons.Count > 0 || limitFor is null || name is null)
            return new BudgetInvalid(reasons);

        return new BudgetValid(new ValidatedBudget(list.AsReadOnly(), name, limitFor));
    }
}
