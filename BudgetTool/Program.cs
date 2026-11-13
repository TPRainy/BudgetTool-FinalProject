using System.Globalization;
using BudgetTool;

const string usage = "Usage: dotnet run -- [--file <json>] [--policy moderate|strict|both|<name>] [--category <name>]"
    + " [--confirm-first] [--min <amount>] [--export <file.csv>]";

var path = "data/transactions.json";
var policyName = "both";
var category = Option.None<string>();
var confirmFirst = false;
var minAmount = 100m;
var exportPath = Option.None<string>();

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--file" && i + 1 < args.Length)
    {
        i++;
        path = args[i];
    }
    else if (args[i] == "--policy" && i + 1 < args.Length)
    {
        i++;
        policyName = args[i];
    }
    else if (args[i] == "--category" && i + 1 < args.Length)
    {
        i++;
        category = Option.Some(args[i]);
    }
    else if (args[i] == "--confirm-first")
    {
        confirmFirst = true;
    }
    else if (args[i] == "--min" && i + 1 < args.Length)
    {
        i++;
        if (!decimal.TryParse(args[i], NumberStyles.Number, CultureInfo.InvariantCulture, out minAmount))
        {
            Console.Error.WriteLine("Invalid --min value '" + args[i] + "': use a number such as 150 or 99.50");
            return 1;
        }
    }
    else if (args[i] == "--export" && i + 1 < args.Length)
    {
        i++;
        exportPath = Option.Some(args[i]);
    }
    else
    {
        Console.Error.WriteLine(usage);
        return 1;
    }
}

var wanted = policyName.Trim().ToLowerInvariant();
var names = wanted == "both" ? new[] { "moderate", "strict" } : new[] { wanted };
var exitCode = 0;

void Record(Error error)
{
    if (error.Kind == ErrorKind.Operational)
        exitCode = 1;
    else if (exitCode == 0)
        exitCode = 2;
}

BudgetData data;
switch (BudgetFile.Load(path))
{
    case Success<BudgetData> loaded:
        data = loaded.Value;
        break;
    case Failure<BudgetData> failed:
        Console.Error.WriteLine(Display.DescribeError(failed.Error));
        Console.Error.WriteLine("No report was produced.");
        return 1;
    default:
        Console.Error.WriteLine("Unknown load result");
        return 1;
}

Console.WriteLine("Input file: " + data.Source);
Console.WriteLine("Accepted records: " + data.Transactions.Count + ", rejected records: " + data.Rejected.Count);
foreach (var error in data.Rejected)
    Console.WriteLine("  REJECTED - " + error);
Console.WriteLine();

foreach (var name in names)
{
    Console.WriteLine("== " + name + " ==");

    var result = confirmFirst
        ? ReportFlow.ConfirmWithoutEvaluation(data.Transactions, name)
        : ReportFlow.Run(data.Transactions, name, category);

    switch (result)
    {
        case Success<ReportView> report:
            foreach (var line in report.Value.Lines)
                Console.WriteLine(line);
            break;
        case Failure<ReportView> failed:
            Console.WriteLine(Display.DescribeError(failed.Error));
            Record(failed.Error);
            break;
        default:
            Console.Error.WriteLine("Unknown report result");
            return 1;
    }

    Console.WriteLine();
}

Console.WriteLine("== Reports ==");

switch (Analysis.Run(data.Transactions, names, minAmount, new NativeUsage()))
{
    case Success<AnalysisResult> analysis:
        foreach (var report in analysis.Value.CategoryReports)
        {
            foreach (var line in Display.FormatCategoryReport(report))
                Console.WriteLine(line);
            Console.WriteLine();
        }

        foreach (var line in Display.FormatLargeExpenses(analysis.Value.LargeExpenses))
            Console.WriteLine(line);
        Console.WriteLine();

        if (exportPath is Some<string> target)
        {
            switch (ReportFile.Save(target.Value, CsvExport.CategoryLines(analysis.Value.CategoryReports)))
            {
                case Success<string> saved:
                    Console.WriteLine("Exported the category report to " + saved.Value);
                    break;
                case Failure<string> failed:
                    Console.WriteLine(Display.DescribeError(failed.Error));
                    Console.WriteLine("The export was not written; an existing file was left unchanged.");
                    Record(failed.Error);
                    break;
            }
        }
        break;
    case Failure<AnalysisResult> failed:
        Console.WriteLine(Display.DescribeError(failed.Error));
        Console.WriteLine("Reports were not produced and nothing was exported.");
        Record(failed.Error);
        break;
    default:
        Console.Error.WriteLine("Unknown analysis result");
        return 1;
}

return exitCode;
