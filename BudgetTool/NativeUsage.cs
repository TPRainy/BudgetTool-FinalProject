namespace BudgetTool;

public sealed class NativeUsage
{
    public const decimal MaxAmount = 1_000_000_000_000m;

    private readonly Func<long, long, long> _usagePercent;

    public NativeUsage() : this(NativeMethods.UsagePercent) { }

    public NativeUsage(Func<long, long, long> usagePercent)
    {
        ArgumentNullException.ThrowIfNull(usagePercent);
        _usagePercent = usagePercent;
    }

    public Result<long> Percent(decimal spent, decimal limit)
    {
        if (spent < 0m || spent > MaxAmount)
            return Result.Fail<long>(NativeErrors.InvalidInput("spent", spent));

        if (limit < 0.01m || limit > MaxAmount)
            return Result.Fail<long>(NativeErrors.InvalidInput("limit", limit));

        var spentCents = spent.ToCents();
        var limitCents = limit.ToCents();

        long percent;
        try
        {
            percent = _usagePercent(spentCents, limitCents);
        }
        catch (DllNotFoundException ex)
        {
            return Result.Fail<long>(NativeErrors.Unavailable(ex.Message));
        }
        catch (EntryPointNotFoundException ex)
        {
            return Result.Fail<long>(NativeErrors.Unavailable(ex.Message));
        }
        catch (BadImageFormatException ex)
        {
            return Result.Fail<long>(NativeErrors.Unavailable(ex.Message));
        }

        if (percent < 0)
            return Result.Fail<long>(NativeErrors.Rejected(spentCents, limitCents));

        return Result.Ok(percent);
    }
}
