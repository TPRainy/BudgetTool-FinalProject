using System.Globalization;

namespace BudgetTool;

public static class CsvExport
{
    public const string CategoryHeader = "policy,category,count,total,share_percent,limit,usage_percent";

    public static IEnumerable<string> CategoryLines(IEnumerable<CategoryReport> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return CategoryLinesEach(reports);
    }

    private static IEnumerable<string> CategoryLinesEach(IEnumerable<CategoryReport> reports)
    {
        yield return CategoryHeader;

        foreach (var report in reports)
        {
            foreach (var row in report.Rows)
            {
                yield return string.Join(",",
                    Field(report.PolicyName),
                    Field(row.Category),
                    row.Count.ToString(CultureInfo.InvariantCulture),
                    Number(row.Total),
                    Number(row.SharePercent),
                    Number(report.Limit),
                    row.UsagePercent.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static string Number(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string Field(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
