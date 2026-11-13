using System.Globalization;

namespace BudgetTool;

public static class Display
{
    public static string Format(string policyName, BudgetSummary summary)
    {
        var rows = new List<string>
        {
            "Policy: " + policyName,
            "  Income: " + Money(summary.Income)
                + "  Expenses: " + Money(summary.Expenses)
                + "  Balance: " + Money(summary.Balance)
        };

        foreach (var line in summary.Lines)
        {
            var status = line.IsOver ? "OVER" : "ok";
            rows.Add($"  {line.Category,-10} spent={Money(line.Spent)} limit={Money(line.Limit)} {status}");
        }

        rows.Add("  Categories over limit: " + summary.OverLimitCount);
        return string.Join("\n", rows);
    }

    public static string DescribeState(ReportState? state) => state switch
    {
        Draft d => "Draft (" + d.Budget.PolicyName + ", " + d.Budget.Transactions.Count + " transactions, not evaluated)",
        Evaluated e => "Evaluated (" + e.Budget.PolicyName + ", balance " + Money(e.Summary.Balance) + ", " + e.Summary.OverLimitCount + " over limit)",
        Confirmed c => "Confirmed (" + c.PolicyName + ", balance " + Money(c.Summary.Balance) + ")",
        _ => "Unknown state"
    };

    public static string DescribeLine(CategoryLine line)
    {
        var status = line.IsOver ? "OVER" : "ok";
        return line.Category + " spent=" + Money(line.Spent) + " limit=" + Money(line.Limit) + " " + status;
    }

    public static string DescribeError(Error error)
    {
        var label = error.Kind switch
        {
            ErrorKind.Domain => "DOMAIN FAILURE",
            ErrorKind.Operational => "OPERATIONAL ERROR",
            _ => "FAILURE"
        };

        return label + " [" + error.Code + "]: " + error.Message;
    }

    public static string DescribeResult(TransitionResult? result) => result switch
    {
        Moved m => "OK -> " + DescribeState(m.Next),
        Refused r => "REFUSED: " + r.Reason,
        _ => "Unknown result"
    };

    public static IReadOnlyList<string> FormatCategoryReport(CategoryReport report)
    {
        var lines = new List<string>
        {
            "Spending by category - " + report.PolicyName
                + " (income " + Money(report.Income) + ", limit " + Money(report.Limit) + " per category)",
            $"  {"category",-14}{"count",6}{"total",12}{"share",9}{"usage",9}"
        };

        foreach (var row in report.Rows)
            lines.Add($"  {row.Category,-14}{row.Count,6}{Money(row.Total),12}{Percent(row.SharePercent),9}{Whole(row.UsagePercent) + "%",9}");

        if (report.Rows.Count == 0)
            lines.Add("  no expenses");

        lines.Add("  Total expenses: " + Money(report.TotalExpenses));
        return lines;
    }

    public static IReadOnlyList<string> FormatLargeExpenses(LargeExpenseReport report)
    {
        var lines = new List<string>
        {
            "Large expenses (amount >= " + Money(report.MinAmount) + ")",
            $"  {"id",-10}{"category",-14}{"amount",12}{"share",9}"
        };

        foreach (var row in report.Rows)
            lines.Add($"  {row.Id,-10}{row.Category,-14}{Money(row.Amount),12}{Percent(row.SharePercent),9}");

        if (report.Rows.Count == 0)
            lines.Add("  no expenses at or above " + Money(report.MinAmount));

        lines.Add("  " + report.Rows.Count + " transactions, total " + Money(report.Total));
        return lines;
    }

    private static string Money(decimal value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    private static string Percent(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture) + "%";

    private static string Whole(long value) => value.ToString(CultureInfo.InvariantCulture);
}