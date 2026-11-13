using System.Text.Json;

namespace BudgetTool;

public static class JsonInputParser
{
    public static Result<IReadOnlyList<RawTransaction?>> Parse(string json, string source)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(source);

        var read = Read(json);
        if (read.FormatError is not null)
            return Result.Fail<IReadOnlyList<RawTransaction?>>(FileErrors.Malformed(source, read.FormatError));

        return Result.Ok(read.Records);
    }

    public static RawReadResult Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("transactions", out var array))
                return Fail("JSON must be an object with a \"transactions\" array");

            if (array.ValueKind != JsonValueKind.Array)
                return Fail("\"transactions\" must be an array");

            var records = array.EnumerateArray().Select(e => ToRaw(e)).ToList();
            return new RawReadResult(records, null);
        }
        catch (JsonException ex)
        {
            return Fail("Invalid JSON: " + ex.Message);
        }
    }

    private static RawReadResult Fail(string message)
    {
        return new RawReadResult(new List<RawTransaction?>(), message);
    }

    private static RawTransaction? ToRaw(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return new RawTransaction(
            Field(element, "id"),
            Field(element, "category"),
            Field(element, "amount"),
            Field(element, "kind"));
    }

    private static string? Field(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }
}