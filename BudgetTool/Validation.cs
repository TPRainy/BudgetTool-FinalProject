using System.Globalization;

namespace BudgetTool;

public static class Validation
{
    public static IEnumerable<RecordCheck> Check(IEnumerable<RawTransaction?> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return CheckEach(raw);
    }

    private static IEnumerable<RecordCheck> CheckEach(IEnumerable<RawTransaction?> raw)
    {
        var number = 0;
        foreach (var item in raw)
        {
            number++;
            if (TryParseTransaction(item, out var transaction, out var error) && transaction is not null)
                yield return new AcceptedRecord(number, transaction);
            else
                yield return new RejectedRecord(number, "Record " + number + ": " + error);
        }
    }
}

public abstract record RecordCheck
{
    private protected RecordCheck() { }
}

public sealed record AcceptedRecord(int Number, Transaction Transaction) : RecordCheck;

public sealed record RejectedRecord(int Number, string Message) : RecordCheck;