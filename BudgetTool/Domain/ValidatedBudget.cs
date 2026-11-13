namespace BudgetTool;

public abstract record BudgetCreation
{
    private protected BudgetCreation() { }
}

public sealed record BudgetValid(ValidatedBudget Budget) : BudgetCreation;

public sealed record BudgetInvalid : BudgetCreation
{
    public BudgetInvalid(IReadOnlyList<Error> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
            throw new ArgumentException("an invalid budget needs at least one error", nameof(errors));

        Errors = errors;
    }

    public IReadOnlyList<Error> Errors { get; }

    public IReadOnlyList<string> Reasons => Errors.Select(e => e.Message).ToList();
}

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
        var errors = new List<Error>();

        if (!list.Any(t => t.Kind == TransactionKind.Income && t.Amount > 0))
            errors.Add(DomainErrors.NoIncome());

        var policy = Policies.Find(policyName);
        if (policy is not Some<Policy>)
            errors.Add(DomainErrors.UnknownPolicy(policyName));

        if (errors.Count == 0 && policy is Some<Policy> found)
            return new BudgetValid(new ValidatedBudget(list.AsReadOnly(), found.Value.Name, found.Value.LimitFor));

        return new BudgetInvalid(errors.AsReadOnly());
    }
}
