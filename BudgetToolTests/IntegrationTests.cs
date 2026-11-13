using Xunit;

namespace BudgetTool.Tests;

public sealed class IntegrationTests : IDisposable
{
    private const string MonthJson = """
        {
          "transactions": [
            { "id": "T1", "category": "salary", "amount": 1500, "kind": "income" },
            { "id": "T2", "category": " Rent ", "amount": 300, "kind": "expense" },
            { "id": "T3", "category": "food", "amount": 150, "kind": "expense" },
            { "id": "T4", "category": "food", "amount": 50, "kind": "expense" },
            { "id": "T5", "category": "fun", "amount": 400, "kind": "expense" },
            { "id": "T3", "category": "food", "amount": 150, "kind": "expense" },
            { "id": "T7", "category": "misc", "amount": -5, "kind": "expense" }
          ]
        }
        """;

    private static readonly string[] BothPolicies = { "moderate", "strict" };

    private readonly string _directory;

    public IntegrationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "budgettool-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void NormalPath_LoadsData_RunsBothReportsWithTheNativeCall_AndExports()
    {
        var input = WriteFile("month.json", MonthJson);
        var output = Path.Combine(_directory, "categories.csv");

        var result = BudgetFile.Load(input)
            .Bind(data => Analysis.Run(data.Transactions, BothPolicies, 100m, new NativeUsage()))
            .Bind(outcome => ReportFile.Save(output, CsvExport.CategoryLines(outcome.CategoryReports))
                .Map(_ => outcome));

        var analysis = Assert.IsType<Success<AnalysisResult>>(result).Value;
        Assert.Equal(new[] { "T5", "T2", "T3" }, analysis.LargeExpenses.Rows.Select(r => r.Id));
        Assert.Equal(850m, analysis.LargeExpenses.Total);
        Assert.Equal(new[]
        {
            "policy,category,count,total,share_percent,limit,usage_percent",
            "moderate,fun,1,400.00,44.44,450.00,89",
            "moderate,rent,1,300.00,33.33,450.00,67",
            "moderate,food,2,200.00,22.22,450.00,44",
            "strict,fun,1,400.00,44.44,300.00,133",
            "strict,rent,1,300.00,33.33,300.00,100",
            "strict,food,2,200.00,22.22,300.00,67",
        }, File.ReadAllLines(output));
    }

    [Fact]
    public void FailurePath_NativeLibraryUnavailable_ReportsIt_AndKeepsThePreviousExport()
    {
        var input = WriteFile("month.json", MonthJson);
        var output = WriteFile("categories.csv", "previous export");
        var failing = new NativeUsage((spent, limit) => throw new DllNotFoundException("simulated: budgetcalc is missing"));

        var result = BudgetFile.Load(input)
            .Bind(data => Analysis.Run(data.Transactions, BothPolicies, 100m, failing))
            .Bind(outcome => ReportFile.Save(output, CsvExport.CategoryLines(outcome.CategoryReports)));

        var error = Assert.IsType<Failure<string>>(result).Error;
        Assert.Equal("native.unavailable", error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Equal("previous export", File.ReadAllText(output));
        Assert.False(File.Exists(output + ".tmp"));
    }

    [Fact]
    public void FailurePath_MalformedInput_StopsBeforeReportsAndExport()
    {
        var input = WriteFile("broken.json", "{ \"transactions\": [ { \"id\": ");
        var output = Path.Combine(_directory, "categories.csv");
        var reportRuns = 0;

        var result = BudgetFile.Load(input)
            .Bind(data =>
            {
                reportRuns++;
                return Analysis.Run(data.Transactions, BothPolicies, 100m, new NativeUsage());
            })
            .Bind(outcome => ReportFile.Save(output, CsvExport.CategoryLines(outcome.CategoryReports)));

        Assert.Equal("file.malformed", Assert.IsType<Failure<string>>(result).Error.Code);
        Assert.Equal(0, reportRuns);
        Assert.False(File.Exists(output));
    }
}
