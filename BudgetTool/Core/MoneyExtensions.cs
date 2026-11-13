namespace BudgetTool;

public static class MoneyExtensions
{
    public static long ToCents(this decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
}
