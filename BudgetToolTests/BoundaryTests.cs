using Xunit;

namespace BudgetTool.Tests;

public sealed class BoundaryTests : IDisposable
{
    private const string ValidJson = """
        {
          "transactions": [
            { "id": "T1", "category": "salary", "amount": 1000, "kind": "income" },
            { "id": "T2", "category": "food", "amount": 200, "kind": "expense" },
            { "id": "T3", "category": "misc", "amount": -5, "kind": "expense" }
          ]
        }
        """;

    private const string TruncatedJson = """
        {
          "transactions": [
            { "id": "T1", "category": "salary", "amount": 1000, "kind": "income" },
            { "id": "T2", "category": "food", "amou
        """;

    private readonly string _directory;

    public BoundaryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "budgettool-tests-" + Guid.NewGuid().ToString("N"));
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

    private static Error FailureOf<T>(Result<T> result) => Assert.IsType<Failure<T>>(result).Error;

    [Fact]
    public void ValidFile_LoadsAcceptedAndRejectedRecords()
    {
        var path = WriteFile("valid.json", ValidJson);

        var data = Assert.IsType<Success<BudgetData>>(BudgetFile.Load(path)).Value;

        Assert.Equal(path, data.Source);
        Assert.Equal(2, data.Transactions.Count);
        Assert.Contains("negative", Assert.Single(data.Rejected));
    }

    [Fact]
    public void MissingFile_IsReported_AndPreviousDataStaysUnchanged()
    {
        var current = Assert.IsType<Success<BudgetData>>(BudgetFile.Load(WriteFile("valid.json", ValidJson))).Value;
        var missingPath = Path.Combine(_directory, "missing.json");

        var error = FailureOf(BudgetFile.Load(missingPath));

        Assert.Equal("file.not_found", error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains("missing.json", error.Message);
        Assert.Equal(2, current.Transactions.Count);
        Assert.False(File.Exists(missingPath));
    }

    [Fact]
    public void MissingDirectory_IsReportedAsNotFound()
    {
        var path = Path.Combine(_directory, "no-such-folder", "data.json");

        var error = FailureOf(BudgetFile.Load(path));

        Assert.Equal("file.not_found", error.Code);
        Assert.Contains("no-such-folder", error.Message);
    }

    [Fact]
    public void MalformedJson_BecomesFailure_WithoutPartialData()
    {
        var current = Assert.IsType<Success<BudgetData>>(BudgetFile.Load(WriteFile("valid.json", ValidJson))).Value;
        var brokenPath = WriteFile("broken.json", TruncatedJson);

        var result = BudgetFile.Load(brokenPath);

        var error = FailureOf(result);
        Assert.Equal("file.malformed", error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains("broken.json", error.Message);
        Assert.IsNotType<Success<BudgetData>>(result);
        Assert.Equal(2, current.Transactions.Count);
        Assert.Equal(TruncatedJson, File.ReadAllText(brokenPath));
    }

    [Fact]
    public void WrongJsonShape_IsMalformed()
    {
        var path = WriteFile("shape.json", "{ \"items\": [] }");

        var error = FailureOf(BudgetFile.Load(path));

        Assert.Equal("file.malformed", error.Code);
        Assert.Contains("transactions", error.Message);
    }

    [Fact]
    public void UnsupportedExtension_IsRejectedBeforeReading()
    {
        var path = WriteFile("data.csv", "id,category,amount,kind");

        var error = FailureOf(BudgetFile.Load(path));

        Assert.Equal("file.unsupported_type", error.Code);
        Assert.Contains("data.csv", error.Message);
    }
}
