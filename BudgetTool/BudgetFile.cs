namespace BudgetTool;

public sealed record BudgetData(string Source, IReadOnlyList<Transaction> Transactions, IReadOnlyList<string> Rejected);

public static class BudgetFile
{
    public static Result<BudgetData> Load(string path) =>
        ReadText(path)
            .Bind(text => JsonInputParser.Parse(text, path))
            .Map(records => ToData(path, records));

    public static Result<string> ReadText(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            return Result.Fail<string>(FileErrors.UnsupportedType(path));

        try
        {
            return Result.Ok(File.ReadAllText(path));
        }
        catch (FileNotFoundException)
        {
            return Result.Fail<string>(FileErrors.NotFound(path));
        }
        catch (DirectoryNotFoundException)
        {
            return Result.Fail<string>(FileErrors.NotFound(path));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<string>(FileErrors.Unreadable(path, ex.Message));
        }
        catch (IOException ex)
        {
            return Result.Fail<string>(FileErrors.Unreadable(path, ex.Message));
        }
    }

    private static BudgetData ToData(string path, IReadOnlyList<RawTransaction?> records)
    {
        var outcome = Validation.ParseAll(records);
        return new BudgetData(path, outcome.Transactions, outcome.Errors);
    }
}
