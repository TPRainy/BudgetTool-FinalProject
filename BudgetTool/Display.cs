using System.Globalization;

namespace BudgetTool;

public static class Display
{
    public static string DescribeState(ReportState? state) => state switch
    {
        Draft d => "Draft (" + d.Budget.PolicyName + ", " + d.Budget.Transactions.Count + " transactions, not evaluated)",
        Evaluated e => "Evaluated (" + e.Budget.PolicyName + ", balance " + Money(e.Summary.Balance) + ", " + e.Summary.OverLimitCount + " over limit)",
        Confirmed c => "Confirmed (balance " + Money(c.Summary.Balance) + ")",
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
}