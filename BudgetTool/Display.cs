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

    public static string DescribeResult(TransitionResult? result) => result switch
    {
        Moved m => "OK -> " + DescribeState(m.Next),
        Refused r => "REFUSED: " + r.Reason,
        _ => "Unknown result"
    };
}