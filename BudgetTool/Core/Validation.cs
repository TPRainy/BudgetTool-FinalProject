using System.Globalization;

namespace BudgetTool;

public static class Validation
{
    public static bool TryParseTransaction(RawTransaction? raw, out Transaction? transaction, out string error)
    {
        transaction = null;

        if (raw is null)
        {
            error = "record is missing (null)";
            return false;
        }
        if (string.IsNullOrWhiteSpace(raw.Id))
        {
            error = "id is missing or blank";
            return false;
        }
        if (string.IsNullOrWhiteSpace(raw.Category))
        {
            error = raw.Id + ": category is missing or blank";
            return false;
        }
        if (string.IsNullOrWhiteSpace(raw.Amount))
        {
            error = raw.Id + ": amount is missing";
            return false;
        }

        var style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
                  | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

        if (!decimal.TryParse(raw.Amount, style, CultureInfo.InvariantCulture, out var amount))
        {
            error = raw.Id + ": amount '" + raw.Amount + "' is not a number";
            return false;
        }
        if (amount < 0)
        {
            error = raw.Id + ": amount must not be negative (got " + amount + ")";
            return false;
        }
        if (string.IsNullOrWhiteSpace(raw.Kind))
        {
            error = raw.Id + ": kind is missing";
            return false;
        }

        TransactionKind kind;
        switch (raw.Kind.Trim().ToLowerInvariant())
        {
            case "income":
                kind = TransactionKind.Income;
                break;
            case "expense":
                kind = TransactionKind.Expense;
                break;
            default:
                error = raw.Id + ": kind '" + raw.Kind + "' must be 'income' or 'expense'";
                return false;
        }

        transaction = new Transaction(raw.Id.Trim(), raw.Category.Trim(), amount, kind);
        error = string.Empty;
        return true;
    }

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

    public static ParseOutcome ParseAll(IEnumerable<RawTransaction?> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var checks = Check(raw).ToList();

        var transactions = checks.OfType<AcceptedRecord>().Select(a => a.Transaction).ToList();
        var errors = checks.OfType<RejectedRecord>().Select(r => r.Message).ToList();

        return new ParseOutcome(transactions, errors);
    }
}

public abstract record RecordCheck
{
    private protected RecordCheck() { }
}

public sealed record AcceptedRecord(int Number, Transaction Transaction) : RecordCheck;

public sealed record RejectedRecord(int Number, string Message) : RecordCheck;