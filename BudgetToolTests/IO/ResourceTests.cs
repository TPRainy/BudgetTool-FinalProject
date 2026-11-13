using Xunit;

namespace BudgetTool.Tests;

public sealed class ResourceTests : IDisposable
{
    private readonly string _directory;

    public ResourceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "budgettool-resource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class ProbeStream : FileStream
    {
        public ProbeStream(string path) : base(path, FileMode.Create, FileAccess.Write, FileShare.None) { }

        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static IEnumerable<string> LinesThatFailAfterTheHeader()
    {
        yield return CsvExport.CategoryHeader;
        throw new IOException("disk is full (simulated)");
    }

    [Fact]
    public void Save_Success_WritesAllLines_AndDisposesTheStream()
    {
        var path = Path.Combine(_directory, "report.csv");
        ProbeStream? opened = null;

        var result = ReportFile.Save(path, new[] { "a,b", "1,2" }, p => opened = new ProbeStream(p));

        Assert.Equal(path, Assert.IsType<Success<string>>(result).Value);
        Assert.Equal(new[] { "a,b", "1,2" }, File.ReadAllLines(path));
        Assert.NotNull(opened);
        Assert.True(opened.Disposed);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_FailureWhileWriting_DisposesTheStream_AndKeepsTheOldFile()
    {
        var path = Path.Combine(_directory, "report.csv");
        File.WriteAllText(path, "previous export");
        ProbeStream? opened = null;

        var result = ReportFile.Save(path, LinesThatFailAfterTheHeader(), p => opened = new ProbeStream(p));

        var error = Assert.IsType<Failure<string>>(result).Error;
        Assert.Equal("file.write_failed", error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.NotNull(opened);
        Assert.True(opened.Disposed);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal("previous export", File.ReadAllText(path));
    }

    [Fact]
    public void Save_ToMissingFolder_OrWrongExtension_IsReported_AndCreatesNothing()
    {
        var missingFolder = Path.Combine(_directory, "no-such-folder");

        var error = Assert.IsType<Failure<string>>(ReportFile.Save(Path.Combine(missingFolder, "report.csv"), new[] { "x" })).Error;
        Assert.Equal("file.write_failed", error.Code);
        Assert.Contains("no-such-folder", error.Message);
        Assert.False(Directory.Exists(missingFolder));

        var wrongType = Path.Combine(_directory, "report.txt");
        Assert.Equal("file.unsupported_type", Assert.IsType<Failure<string>>(ReportFile.Save(wrongType, new[] { "x" })).Error.Code);
        Assert.False(File.Exists(wrongType));
    }

    [Fact]
    public void Load_ReleasesTheInputFile_AfterSuccessAndAfterMalformedContent()
    {
        var good = Path.Combine(_directory, "good.json");
        var bad = Path.Combine(_directory, "bad.json");
        File.WriteAllText(good, "{ \"transactions\": [] }");
        File.WriteAllText(bad, "{ \"transactions\": [");

        Assert.IsType<Success<BudgetData>>(BudgetFile.Load(good));
        Assert.IsType<Failure<BudgetData>>(BudgetFile.Load(bad));

        using (new FileStream(good, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        using (new FileStream(bad, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        File.Delete(good);
        File.Delete(bad);
        Assert.False(File.Exists(good));
        Assert.False(File.Exists(bad));
    }
}
