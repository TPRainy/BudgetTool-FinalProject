namespace BudgetTool;

public enum TransactionKind { Income, Expense }

public sealed record RawTransaction(string? Id, string? Category, string? Amount, string? Kind);
public sealed record Transaction(string Id, string Category, decimal Amount, TransactionKind Kind);
public sealed record CategoryLine(string Category, decimal Spent, decimal Limit, bool IsOver);
public sealed record Evaluation(decimal Income, decimal Expenses, IReadOnlyList<CategoryLine> Lines);
public sealed record BudgetSummary(decimal Income, decimal Expenses, decimal Balance, IReadOnlyList<CategoryLine> Lines, int OverLimitCount);
public sealed record ParseOutcome(IReadOnlyList<Transaction> Transactions, IReadOnlyList<string> Errors);
public sealed record RawReadResult(IReadOnlyList<RawTransaction?> Records, string? FormatError);