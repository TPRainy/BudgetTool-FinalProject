namespace BudgetTool;

public static class ReportFile
{
    public static Result<string> Save(string path, IEnumerable<string> lines) =>
        Save(path, lines, OpenForWriting);

    public static Result<string> Save(string path, IEnumerable<string> lines, Func<string, Stream> open)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(open);

        if (!string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
            return Result.Fail<string>(FileErrors.UnsupportedType(path, ".csv"));

        var temporary = path + ".tmp";
        try
        {
            using (var stream = open(temporary))
            using (var writer = new StreamWriter(stream))
            {
                foreach (var line in lines)
                    writer.WriteLine(line);
            }

            File.Move(temporary, path, overwrite: true);
            return Result.Ok(path);
        }
        catch (IOException ex)
        {
            return Result.Fail<string>(FileErrors.WriteFailed(path, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail<string>(FileErrors.WriteFailed(path, ex.Message));
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static Stream OpenForWriting(string path) =>
        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
}
