using System.Globalization;

namespace BudgetTool;

public static class DomainErrors
{
    public static Error NoIncome() => new(
        "budget.no_income",
        "at least one income transaction with an amount above 0 is required",
        ErrorKind.Domain);

    public static Error UnknownPolicy(string? name) => new(
        "budget.unknown_policy",
        "unknown policy '" + (name ?? "(none)") + "' (known: " + string.Join(", ", Policies.Known) + ")",
        ErrorKind.Domain);

    public static Error InvalidBudget(IReadOnlyList<Error> errors) => new(
        "budget.invalid",
        errors.Count + " budget rules broken: " + string.Join("; ", errors.Select(e => e.Code + ": " + e.Message)),
        ErrorKind.Domain);

    public static Error NotEvaluated() => new(
        "report.not_evaluated",
        "cannot confirm: the report has not been evaluated yet",
        ErrorKind.Domain);

    public static Error AlreadyEvaluated() => new(
        "report.already_evaluated",
        "the report is already evaluated",
        ErrorKind.Domain);

    public static Error AlreadyConfirmed() => new(
        "report.already_confirmed",
        "the report is already confirmed",
        ErrorKind.Domain);

    public static Error OverLimit(IEnumerable<CategoryLine> overLines)
    {
        var lines = overLines.ToList();
        var details = lines.Select(l => l.Category + ": spent " + Money(l.Spent) + ", limit " + Money(l.Limit));

        return new Error(
            "report.over_limit",
            "cannot confirm: categories over limit: " + lines.Count + " (" + string.Join("; ", details) + ")",
            ErrorKind.Domain);
    }

    public static Error UnknownState() => new(
        "report.unknown_state",
        "unknown or missing state",
        ErrorKind.Domain);

    public static Error CategoryNotFound(string requested, IEnumerable<string> available)
    {
        var known = available.ToList();
        var hint = known.Count == 0
            ? "the report has no expense categories"
            : "available: " + string.Join(", ", known);

        return new Error(
            "lookup.category_not_found",
            "category '" + requested + "' was not found in the report (" + hint + ")",
            ErrorKind.Domain);
    }

    public static Error InvalidThreshold(decimal minAmount) => new(
        "report.invalid_threshold",
        "the minimum amount for large expenses must not be negative (got " + Money(minAmount) + ")",
        ErrorKind.Domain);

    private static string Money(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
