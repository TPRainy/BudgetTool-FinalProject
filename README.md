# Budget Summary - Assignment 8: Extension Methods and LINQ

This snapshot extends the Assignment 7 project. The pure pipeline (Assignment 5), the validated factory and safe state transitions (Assignment 6) and the explicit `Result`/`Option` failures (Assignment 7) are still active. Assignment 8 adds two LINQ reports, two custom extension methods, a curried policy function, a lazy (`yield`) validation step, a safe CSV export and a small native C library called through P/Invoke. Earlier descriptions are kept for history in `README_Assignment5.md`, `README_Assignment6.md` and `README_Assignment7.md`.

## 1. Input, domain rules and workflow

**Input:** `data/transactions.json`, an object with a `transactions` array (`id`, `category`, `amount`, `kind` = `income`/`expense`). Invalid records are rejected with a message (Assignment 5), duplicated ids are counted once and categories are trimmed and lower-cased (`Pipeline.Normalize`).

**Domain rules**

| Rule | Where | Failure |
|---|---|---|
| A budget needs income above 0 | `ValidatedBudget.Create` | `budget.no_income` |
| The policy must be `moderate` (30% of income per category) or `strict` (20%) | `ValidatedBudget.Create`, `Policies.Find` | `budget.unknown_policy` |
| A report is confirmed only after evaluation and only if no category is over its limit | `Workflow.Confirm` | `report.not_evaluated`, `report.over_limit` |
| A requested focus category must exist | `CategoryLookup.Require` | `lookup.category_not_found` |
| **New:** the minimum amount for large expenses must not be negative | `Analysis.Run` | `report.invalid_threshold` |
| **New:** usage needs spent 0.00 to 1 000 000 000 000.00 and a limit of 0.01 to 1 000 000 000 000.00 | `NativeUsage.Percent` | `usage.invalid_input` |
| **New:** the native library must load and expose `bt_usage_percent` | `NativeUsage.Percent` | `native.unavailable` (operational) |
| **New:** the export target must be a `.csv` file in an existing folder | `ReportFile.Save` | `file.unsupported_type`, `file.write_failed` (operational) |

**Workflow**

```
JSON file -> BudgetFile.Load (boundary) -> BudgetData snapshot
   |-- per policy: ReportFlow.Run (Assignment 7 chain: factory -> Draft -> Evaluated -> Confirmed)
   |-- Analysis.Run: Normalize once -> Report 1 per policy (+ native usage) and Report 2 -> AnalysisResult
   '-- optional: CsvExport.CategoryLines (lazy lines) -> ReportFile.Save (using + temporary file)
Program.cs prints every outcome once and sets the exit code.
```

**Exit codes:** `0` everything succeeded, `2` at least one domain failure (for example the strict report cannot be confirmed), `1` operational error (file, export or native library) or wrong arguments.

## 2. Custom extension methods (Task 1)

| Method | Receiver | Returns | Behaviour | Used in |
|---|---|---|---|---|
| `MoneyExtensions.ToCents(this decimal amount)` | a money amount (`decimal`) | `long` cents | multiplies by 100 and rounds half away from zero: `450m` -> `45000`, `0.005m` -> `1`, `0.004m` -> `0` | `NativeUsage.Percent` before the native call |
| `ResultExtensions.Sequence<T>(this IEnumerable<Result<T>> results)` | a sequence of `Result<T>` values | `Result<IReadOnlyList<T>>` | `Success` with every value in the original order, or the **first** `Failure`; items after the first failure are not read | `Analysis.Run` (one category report per policy) and `Analysis.AddUsage` (one native call per category) |

**Why these receivers**

- `ToCents` belongs on `decimal` because every money value in the domain is a `decimal`, and converting an amount into the smallest currency unit is a property of the amount itself. It also fixes the rounding rule of the native contract in one place. It has no side effects.
- `Sequence` belongs on `IEnumerable<Result<T>>` because it only makes sense for a collection of outcomes: it turns many results into one result. It is in the same family as `Map` and `Bind` (Assignment 7, still used) and it does not change any shared state.

## 3. LINQ reports (Task 2)

Both reports are pure functions in `Reports.cs`; they never print or write files. `Display.cs` formats them, `CsvExport.cs` and `ReportFile.cs` export them.

### Report 1: spending by category (`Reports.SpendingByCategory` + `Analysis.AddUsage`)

| | |
|---|---|
| **Intended user** | the person managing the monthly budget: which categories use most of their limit |
| **Input** | validated and normalized transactions from the JSON file, plus the policy (its configured limit function) |
| **Filter** | expenses only |
| **Query stages** | `Where(expense)` -> `GroupBy(category)` -> `Select` with `Count()` and `Sum(amount)` and share -> `OrderByDescending(total)` -> `ThenBy(category, ordinal)` -> `ToList()`; then one native call per row for usage |
| **Output fields** | category, count, total, share of all expenses (%, 2 decimals, half away from zero), usage of the category limit (%, whole number, from C) |
| **Tie rule** | equal totals are ordered by category name (ordinal) |
| **Expected result** (`transactions.json`) | moderate: fun 1 / 400.00 / 44.44% / 89%, rent 1 / 300.00 / 33.33% / 67%, food 2 / 200.00 / 22.22% / 44%; strict: the same totals with usage 133%, 100%, 67% |

### Report 2: large expenses (`Reports.LargeExpenses`)

| | |
|---|---|
| **Intended user** | the same person (or a reviewer) checking individual big payments |
| **Input** | validated and normalized transactions, minimum amount `--min` (default 100.00, inclusive) |
| **Filter** | `Kind == Expense` and `Amount >= min` |
| **Query stages** | `Where` -> `Where(amount >= min)` -> `OrderByDescending(amount)` -> `ThenBy(id, ordinal)` -> `Select` to a row -> `ToList()`; `Sum` for the total |
| **Output fields** | id, category, amount, share of all expenses (%); number of rows and their total |
| **Tie rule** | equal amounts are ordered by id (ordinal) |
| **Expected result** (`transactions.json`) | T5 fun 400.00 44.44%, T2 rent 300.00 33.33%, T3 food 150.00 16.67%; 3 transactions, total 850.00. With `--min 1000`: no rows, total 0.00 |

Edge cases: an empty dataset gives no rows; a dataset with only income gives no category rows; a total of 0 gives a share of 0 (no division by zero); rejected records (negative amount, `abc`) and duplicated ids never reach the reports.

## 4. Curried configuration (Task 3)

```csharp
public static Func<decimal, decimal> LimitShare(decimal share)   // decimal -> (decimal -> decimal)
    => income => income * share;

public static Func<decimal, decimal> Moderate { get; } = LimitShare(0.30m);  // partial application
public static Func<decimal, decimal> Strict { get; } = LimitShare(0.20m);    // partial application
```

- `LimitShare` receives the share first and the income in a second call: `Policies.LimitShare(0.30m)(1000m)` = `300`.
- Fixing the share gives two configured functions, `Moderate` and `Strict`. They are passed into the workflow as values (`Policies.Find` -> `ValidatedBudget.LimitFor` -> `Pipeline.Run` and `Analysis`); no global setting is changed to switch policy.
- **Documented different result on the same data** (`transactions.json`, income 1500): moderate limit 450.00, 0 categories over limit, report confirmed, `fun` usage 89%; strict limit 300.00, 1 category over limit (`fun`), confirmation refused, `fun` usage 133%.
- A share outside `(0, 1]` throws `ArgumentOutOfRangeException` when the function is configured (a programming error, not user input).

## 5. Deferred execution (Task 4)

`Validation.Check(raw)` (`Validation.cs`) is a `yield return` workflow that produces `AcceptedRecord` / `RejectedRecord` values, one per raw record.

- **Creating it runs nothing.** `Check` validates its argument immediately and returns the iterator `CheckEach`; no record is validated until enumeration starts.
- **Iteration triggers processing.** Each `MoveNext` validates exactly one record. `First()` validates only the first record.
- **Materialisation point:** `Validation.ParseAll` calls `Check(raw).ToList()` once. The resulting `BudgetData` snapshot is reused by the Assignment 7 flow for each policy, by both reports and by the export.
- **Repeated enumeration before materialisation** validates every record again and creates new `AcceptedRecord`/`RejectedRecord` objects each time. It never re-reads the file: the raw records are already an in-memory list and the JSON document is already disposed. **After materialisation** the list can be enumerated any number of times without validating again.
- The tests use a probe source (a counting `yield` method in the test class), so the production code contains no diagnostic counters: 0 records pulled after creation, 1 after `First()`, 6 after two `Count()` calls, 9 after `ToList()`, still 9 after reusing the snapshot.

A second lazy sequence, `CsvExport.CategoryLines`, is deliberately **not** materialised: its lines are streamed straight into the open `StreamWriter` (see section 6).

## 6. Resources: ownership, release and retention (Tasks 5 and 6)

| Resource | Created in | Owner | Released | Failure path |
|---|---|---|---|---|
| Input file handle | `File.ReadAllText` in `BudgetFile.ReadText` (convenience API) | the API itself | before `ReadAllText` returns | the API closes its handle on every path (a missing file never opens one); the exception becomes `file.not_found` / `file.unreadable` |
| `JsonDocument` (pooled buffers) | `JsonInputParser.Read` | `Read` (`using var document`) | at the end of `Read` | if `JsonDocument.Parse` throws `JsonException`, no document exists; any exception after it is created is covered by `using` |
| `FileStream` + `StreamWriter` | `ReportFile.Save` (stream from the `open` factory) | `Save` (two `using` blocks) | at the end of the `using` blocks, before `File.Move` | disposed by `using`, then the temporary file is deleted in `finally`; the target file is untouched |
| Native library `budgetcalc` | the .NET runtime on the first P/Invoke call | the runtime | when the process exits | a failed load becomes `native.unavailable` |

- `ReportFile.Save` writes to `<target>.tmp` and moves it over the target only after the writer is closed. A failure while writing therefore never leaves a half-written report and keeps any previous export.
- Lines are produced lazily **inside** the `using` scope, so the writer is open for the whole enumeration; if the line producer throws, the writer is still disposed.
- `JsonInputParser.Read` copies every element into `RawTransaction` strings and calls `ToList()` **before** the `JsonDocument` is disposed. Without that `ToList()` the deferred `Select` would read a disposed document.
- The native function uses only 64-bit integers: no native memory is allocated and no handle is returned, so there is nothing to free.

**Resource note (Task 6)**

- **Where datasets are materialised:** the raw records (`JsonInputParser.Read`, `ToList`), the validated transactions (`ParseAll`, `ToList`), the normalized list (`Pipeline.Normalize`, once per `Analysis.Run`), each report's rows (`ToList`, so display and export reuse them) and `Sequence` (a list of the successful values).
- **Deferred queries and closures:** the `Check` iterator keeps a reference to the raw list only while it is enumerated; no deferred query is stored in a field. The configured functions `Moderate`/`Strict` capture one `decimal` each and are static for the life of the process (two small delegates). The `NativeUsage` adapter keeps only a delegate to the `extern` method.
- **Long-lived collections, caches, events:** none are used. The only static data are `Policies.Known` (two strings) and the two policy functions. There are no event subscriptions and no caches, so nothing can grow during a run.
- **Design decision:** `Analysis.Run` normalizes the transactions **once** and passes the same list to every policy's category report and to the large-expense report, instead of normalizing (and allocating a new list) per report and per policy. Report rows are materialised once and shared by the console output and the CSV export.
- **Streaming vs snapshot:** CSV lines are streamed (one pass, constant memory, nothing to keep after writing, but they cannot be re-read without recomputing and must stay inside the open writer scope). Transactions and report rows are snapshots (computed once and reused several times, at the cost of keeping a small monthly dataset in memory). The input file is read whole: the handle is closed immediately, which matters more here than the memory of a small JSON file.
- File handles are released by `using` or by the convenience API, never by waiting for garbage collection.

## 7. Native C library (Tasks 7 to 9)

### Function contract

| | |
|---|---|
| Name | `bt_usage_percent` (C linkage, `cdecl`, exported from `budgetcalc`) |
| Declaration | `int64_t bt_usage_percent(int64_t spent_cents, int64_t limit_cents);` (`native/budgetcalc.h`) |
| Units | money in **cents** (integer smallest currency unit); C# converts with `ToCents` (round half away from zero) |
| Valid range | `spent_cents` 0 to 10^14; `limit_cents` 1 to 10^14 (10^14 cents = 1 000 000 000 000.00) |
| Result | whole percent of the limit used, rounded half up: `(spent_cents * 100 + limit_cents / 2) / limit_cents`; `spent_cents * 100` is at most 10^16, far below the `int64_t` maximum, so there is no overflow |
| Invalid input | returns `-1` (`BT_INVALID_INPUT`); the C# adapter validates first (`usage.invalid_input`) and treats any negative answer as `usage.rejected` |
| Manually checked example | spent 400.00 = 40 000 cents, limit 300.00 = 30 000 cents: 40 000 x 100 = 4 000 000; + 15 000 = 4 015 000; / 30 000 = 133 (exact value 133.33%). Also 1 of 8 cents = 12.5% -> 13 (half up) |
| Types | `int64_t` and C# `long` are exactly 64 bits on every platform. C `long` was avoided because it is 32 bits on Windows and 64 bits on Linux |
| Used for | the `usage` column of report 1 and of the exported CSV (`Analysis.AddUsage`) |

### C# binding and adapter

```csharp
// NativeMethods.cs
[DllImport("budgetcalc", EntryPoint = "bt_usage_percent", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
[DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
public static extern long UsagePercent(long spentCents, long limitCents);
```

- The library name has no extension: the runtime looks for `budgetcalc.dll` on Windows and `libbudgetcalc.so` on Linux, in the folder of `BudgetTool.dll`.
- Both arguments and the return value are blittable `long` values, so `DllImport` needs no unsafe code, no `AllowUnsafeBlocks` and no marshalling attributes.
- `NativeUsage.Percent(spent, limit)` is the adapter. It validates the amounts, converts them with `ToCents`, calls the function and turns `DllNotFoundException`, `EntryPointNotFoundException` and `BadImageFormatException` into `native.unavailable`. The default constructor binds the real function; the second constructor accepts another `Func<long, long, long>` so that tests can inject a failing binding.

### Platform, build and loading

| | |
|---|---|
| Declared platform | **Windows x64** (also included: a Linux x64 build) |
| Verified on | Windows, 64-bit .NET 10.0.12 (all 86 tests, including the real native call, and the demonstrations below) |
| Prebuilt binary | `BudgetTool/native/runtimes/win-x64/budgetcalc.dll` (x86-64, 2 KB, one export `bt_usage_percent`, no imports, no entry point) |
| Compiler of the prebuilt DLL | clang 18.1.3 with lld-link 18 (LLVM), target `x86_64-pc-windows-msvc`, cross-compiled on Ubuntu 24.04 x64 |
| Linux binary | `BudgetTool/native/runtimes/linux-x64/libbudgetcalc.so`, gcc 13.3.0 |
| Build script | `bash native/build.sh win-x64` or `bash native/build.sh linux-x64` (run from the `BudgetTool` folder, for example in Git Bash) |
| Exact Windows command | `clang --target=x86_64-pc-windows-msvc -O2 -ffreestanding -nostdlib -shared -fuse-ld=lld -Wl,-noentry -o runtimes/win-x64/budgetcalc.dll budgetcalc.c` (in `native/`) |
| Exact Linux command | `gcc -O2 -shared -fPIC -fvisibility=hidden -o runtimes/linux-x64/libbudgetcalc.so budgetcalc.c` (in `native/`) |
| Prerequisite to rebuild on Windows | LLVM (clang and lld-link) on `PATH`; not needed to run the project, because the DLL is included |
| How it is found after a clean build | `BudgetTool.csproj` marks the binary for the current OS with `CopyToOutputDirectory` and `Link`, so `dotnet build` copies it to `BudgetTool/bin/Debug/net10.0/` and, through the project reference, to `BudgetToolTests/bin/Debug/net10.0/` |

The `.gitattributes` file keeps `build.sh` with LF line endings and marks `.dll`/`.so` as binary.

**Integration difficulty and resolution:** the machine used to build the library had no MinGW or Visual Studio C toolchain, and the Microsoft C runtime libraries that a normal Windows DLL links against were not available. The DLL was therefore cross-compiled with clang and lld-link as a freestanding library: `-ffreestanding -nostdlib` removes the C runtime and `-Wl,-noentry` builds a DLL without `DllMain`. This works because `bt_usage_percent` uses only integer arithmetic. The result was checked with `llvm-readobj` (one export, no imported DLLs). The same source was built for Linux with gcc and checked with 15 real calls before the C# tests ran on Windows. The link options are written with `-` instead of `/` because Git Bash converts arguments that start with `/` into file paths.

## 8. Build, run and test

- **.NET SDK:** 10.0 (`dotnet --version`); tests ran on the 64-bit .NET 10.0.12 runtime.
- **Working directory:** the `BudgetTool` folder (it contains `BudgetTool.csproj`, `data` and `native`).
- **Prerequisites:** Windows x64 (or Linux x64); internet access on the first build to restore the xUnit packages. No C compiler is needed to run, because the binary is included.

```
cd BudgetTool
dotnet build BudgetTool.csproj
dotnet test ../BudgetToolTests
dotnet run -- --policy both --export category_report.csv
```

Arguments: `--file <json>` (default `data/transactions.json`), `--policy moderate|strict|both|<name>` (default `both`), `--category <name>`, `--confirm-first`, **new** `--min <amount>` (default 100), **new** `--export <file.csv>`.

## 9. Demonstrations

| Case | Command (from `BudgetTool`) | Result |
|---|---|---|
| Normal: both reports, native call, export | `dotnet run -- --policy both --export category_report.csv` | reports printed, `category_report.csv` written (exit code 2 because strict confirmation is refused, an Assignment 7 rule) |
| No matching records | `dotnet run -- --min 1000` | report 2 is empty |
| Domain failure | `dotnet run -- --min -5` | `report.invalid_threshold`, no reports, nothing exported |
| Export failure (consistent state) | `dotnet run -- --export missing_folder/report.csv` | `file.write_failed`, no file or folder created |
| Earlier failures still work | `--policy lenient`, `--file data/no_income.json`, `--file data/missing.json`, `--file data/invalid.json` | `budget.unknown_policy`, `budget.no_income`, `file.not_found`, `file.malformed` |

**Recorded output** (Windows)

```
$ dotnet run -- --policy both --export category_report.csv
Input file: data/transactions.json
Accepted records: 6, rejected records: 2
  REJECTED - Record 7: T7: amount must not be negative (got -5)
  REJECTED - Record 8: T8: amount 'abc' is not a number

== moderate ==
Policy: moderate
  Income: 1500.00  Expenses: 900.00  Balance: 600.00
  fun        spent=400.00 limit=450.00 ok
  rent       spent=300.00 limit=450.00 ok
  food       spent=200.00 limit=450.00 ok
  Categories over limit: 0
  Status: Confirmed (moderate, balance 600.00)

== strict ==
DOMAIN FAILURE [report.over_limit]: cannot confirm: categories over limit: 1 (fun: spent 400.00, limit 300.00)

== Reports ==
Spending by category - moderate (income 1500.00, limit 450.00 per category)
  category       count       total    share    usage
  fun                1      400.00   44.44%      89%
  rent               1      300.00   33.33%      67%
  food               2      200.00   22.22%      44%
  Total expenses: 900.00

Spending by category - strict (income 1500.00, limit 300.00 per category)
  category       count       total    share    usage
  fun                1      400.00   44.44%     133%
  rent               1      300.00   33.33%     100%
  food               2      200.00   22.22%      67%
  Total expenses: 900.00

Large expenses (amount >= 100.00)
  id        category            amount    share
  T5        fun                 400.00   44.44%
  T2        rent                300.00   33.33%
  T3        food                150.00   16.67%
  3 transactions, total 850.00

Exported the category report to category_report.csv
```

Content of `category_report.csv` for this data (the same lines are asserted in `IntegrationTests.NormalPath_...`):

```
policy,category,count,total,share_percent,limit,usage_percent
moderate,fun,1,400.00,44.44,450.00,89
moderate,rent,1,300.00,33.33,450.00,67
moderate,food,2,200.00,22.22,450.00,44
strict,fun,1,400.00,44.44,300.00,133
strict,rent,1,300.00,33.33,300.00,100
strict,food,2,200.00,22.22,300.00,67
```

```
$ dotnet run -- --min 1000
...
Large expenses (amount >= 1000.00)
  id        category            amount    share
  no expenses at or above 1000.00
  0 transactions, total 0.00

$ dotnet run -- --min -5
...
== Reports ==
DOMAIN FAILURE [report.invalid_threshold]: the minimum amount for large expenses must not be negative (got -5.00)
Reports were not produced and nothing was exported.

$ dotnet run -- --export missing_folder/report.csv
...
OPERATIONAL ERROR [file.write_failed]: report file 'missing_folder/report.csv' was not written: Could not find a part of the path '...\BudgetTool\missing_folder\report.csv.tmp'.
The export was not written; an existing file was left unchanged.
```

(`...` stands for the unchanged input and Assignment 7 sections shown above.)

## 10. Requirement map

| Requirement | Files and methods |
|---|---|
| Two custom extension methods used by the application | `MoneyExtensions.ToCents` (used in `NativeUsage.Percent`), `ResultExtensions.Sequence` (used in `Analysis.Run`, `Analysis.AddUsage`) |
| Two LINQ reports with Where, Select, OrderBy(Descending), ThenBy, GroupBy, Count/Sum | `Reports.SpendingByCategory`, `Reports.LargeExpenses` (`Reports.cs`) |
| Reports on actual JSON data, separate from display and export | `BudgetFile.Load` -> `Analysis.Run`; display in `Display.FormatCategoryReport/FormatLargeExpenses`; export in `CsvExport`, `ReportFile` |
| Curried function and two partial applications passed into the workflow | `Policies.LimitShare`, `Policies.Moderate`, `Policies.Strict`, `Policies.Find` -> `ValidatedBudget.LimitFor` |
| `yield return` workflow and deliberate materialisation | `Validation.Check`/`CheckEach`, `Validation.ParseAll` (`ToList`); `CsvExport.CategoryLines` (streamed) |
| Safe disposal with `using`, success and failure paths | `ReportFile.Save`, `JsonInputParser.Read`; convenience API `File.ReadAllText` in `BudgetFile.ReadText` |
| Resource note | section 6 |
| Native C library with an exported function | `native/budgetcalc.c`, `native/budgetcalc.h` (`bt_usage_percent`) |
| Build script / compiler command, binary location | `native/build.sh`, `native/runtimes/<rid>/`, `BudgetTool.csproj` (copy to output) |
| P/Invoke binding | `NativeMethods.UsagePercent` (`DllImport`) |
| Adapter with validation and Result failures | `NativeUsage.Percent`, `NativeErrors.cs` |
| Native value used in a real workflow | `Analysis.AddUsage` -> `CategoryUsageRow.UsagePercent` -> console report and CSV |
| Earlier pipeline, safe transitions and Result failures kept | `Pipeline`, `Workflow`, `ReportFlow`, `DomainErrors`, `FileErrors` |
| UI, files and native loading outside pure queries | `Reports.cs` is pure; I/O in `Program.cs`, `BudgetFile`, `ReportFile`; native call only in `NativeUsage` |

## 11. Automated tests (xUnit)

**Latest test outcome:** total: 86; failed: 0; succeeded: 86; skipped: 0 (Windows, .NET 10.0.12).

Earlier tests kept unchanged: 17 in `PipelineTests.cs` (Assignment 5), 16 in `WorkflowTests.cs` (Assignment 6), 23 in `ResultTests.cs`, `ReportFlowTests.cs` and `BoundaryTests.cs` (Assignment 7, including the missing-file and malformed-file tests with temporary folders). New for Assignment 8: 30 tests.

| # | Test | Area | Expected result |
|---|---|---|---|
| | **`ReportsTests.cs`** | | |
| 1 | `SpendingByCategory_GroupsCountsSumsAndOrders` | Reports: grouping and totals | fun/rent/food, counts 1/1/2, totals 400/300/200, shares 44.44/33.33/22.22 |
| 2 | `SpendingByCategory_TiedTotals_AreOrderedByCategoryName` | Reports: ties | equal totals give books, cafe, travel for two different input orders |
| 3 | `SpendingByCategory_EmptyInput_AndOnlyIncome_GiveNoRows` | Reports: empty / no matches | no rows |
| 4 | `LargeExpenses_FiltersOrdersAndTotals` | Reports: normal | T5, T2, T3; total 850; shares 44.44 and 16.67 |
| 5 | `LargeExpenses_MinimumIsInclusive_AndTiedAmountsAreOrderedById` | Reports: ties, boundary | 150 is included, 149.99 is not; E1 before E3 |
| 6 | `LargeExpenses_NoMatches_AndEmptyInput_GiveEmptyReport` | Reports: no matches / empty | no rows, total 0 |
| 7 | `ZeroAmounts_AreValidRows_WithZeroShare` | Reports: edge values | a 0.00 expense is a row with share 0; share of 0/0 is 0 |
| 8 | `InvalidRecords_AndDuplicates_NeverReachTheReports` | Reports: invalid data | negative and non-numeric records rejected, duplicate id counted once |
| 9 | `Analysis_ReusesExplicitDomainFailures` | Reports: failures | `budget.no_income`, `budget.unknown_policy`, `report.invalid_threshold` |
| | **`ExtensionAndCurryingTests.cs`** | | |
| 10 | `ToCents_ConvertsMoneyToWholeCents_RoundingHalfAwayFromZero` | Extensions | 450 -> 45000, 0.005 -> 1, 0.004 -> 0, 123.455 -> 12346, -0.005 -> -1 |
| 11 | `Sequence_AllSuccesses_GivesValuesInOrder` | Extensions | `[1, 2, 3]`; empty input gives an empty success |
| 12 | `Sequence_StopsAtFirstFailure_AndDoesNotProduceTheRest` | Extensions | the same `Error` object; only 2 of 3 items produced |
| 13 | `LimitShare_IsCurried_AndPartialApplicationsGiveDifferentLimits` | Configured functions | `LimitShare(0.30)(1000)` = 300; Moderate 300 vs Strict 200 on the same income; invalid shares throw |
| 14 | `ConfiguredLimits_GiveDifferentResultsOnTheSameData` | Configured functions | over-limit count 0 vs 1; limits 450 vs 300; `fun` usage 89% vs 133% |
| | **`DeferredExecutionTests.cs`** | | |
| 15 | `Check_DoesNothingWhenCreated_AndProcessesRecordsDuringIteration` | Deferred execution | 0 records pulled after creation, 1 after `First()` |
| 16 | `RepeatedEnumeration_RunsAgain_ButASnapshotIsReused` | Deferred execution | 6 pulled after two counts, 9 after `ToList()`, still 9 after reuse |
| 17 | `ParseAll_MaterialisesOnce_AndChecksArgumentsImmediately` | Deferred execution | each record pulled once; `null` argument throws immediately |
| | **`ResourceTests.cs`** (own temporary folder, deleted in `Dispose`) | | |
| 18 | `Save_Success_WritesAllLines_AndDisposesTheStream` | Resource lifetime | file written, stream disposed, no `.tmp` left |
| 19 | `Save_FailureWhileWriting_DisposesTheStream_AndKeepsTheOldFile` | Resource lifetime: failure | `file.write_failed`, stream disposed, no `.tmp`, old content kept |
| 20 | `Save_ToMissingFolder_OrWrongExtension_IsReported_AndCreatesNothing` | File boundary | `file.write_failed` / `file.unsupported_type`, nothing created |
| 21 | `Load_ReleasesTheInputFile_AfterSuccessAndAfterMalformedContent` | Resource lifetime: convenience API | the input files can be opened exclusively and deleted after loading |
| | **`NativeTests.cs`** | | |
| 22 | `RealNativeCall_ReturnsTheManuallyCalculatedValue` | Native: real call | 40000/30000 -> 133, 1/8 -> 13, equal -> 100, 0 -> 0, invalid -> -1 |
| 23 | `Adapter_RealCall_ConvertsMoneyToCents` | Native: real call through the adapter | 400/300 -> 133, 400/450 -> 89, 0/300 -> 0 |
| 24 | `Adapter_InvalidInput_IsRejectedBeforeTheNativeCall` | Native: validation | `usage.invalid_input` four times, the binding is never called |
| 25 | `Adapter_NegativeNativeAnswer_BecomesRejectedFailure` | Native: adapter | `usage.rejected` |
| 26 | `MissingLibrary_RealLoadFailure_BecomesResultFailure` | Native: load failure | a real `DllImport` of a library that does not exist gives `native.unavailable` (Operational) |
| 27 | `MissingExport_BindingFailure_BecomesResultFailure` | Native: binding failure | a missing export name gives `native.unavailable` |
| | **`IntegrationTests.cs`** (own temporary folder, deleted in `Dispose`) | | |
| 28 | `NormalPath_LoadsData_RunsBothReportsWithTheNativeCall_AndExports` | Complete workflow: normal | JSON file -> both reports with real native usage -> CSV with the 7 expected lines |
| 29 | `FailurePath_NativeLibraryUnavailable_ReportsIt_AndKeepsThePreviousExport` | Complete workflow: failure | `native.unavailable`; the previous CSV is unchanged, no `.tmp` |
| 30 | `FailurePath_MalformedInput_StopsBeforeReportsAndExport` | Complete workflow: failure | `file.malformed`; reports never run, no CSV created |

The fresh load failure (test 26) does not depend on deleting a file: it binds to a library name that has never existed, so the runtime really tries and fails to load it. The real call (tests 22, 23, 28) uses the actual `budgetcalc.dll`.

## 12. Team and contributions

| Name | Group | Implemented logic | Tests | Documentation |
|---|---|---|---|---|
| Arlan Narushev | IT-2513 | `Reports.cs` (both LINQ reports), `Analysis.cs` (integration of reports, policies and native usage), `Policies.LimitShare` with `Moderate`/`Strict` (currying), `ResultExtensions.Sequence`, `Display.FormatCategoryReport/FormatLargeExpenses`, `Program.cs` (`--min`, `--export`, reports section, exit codes), `DomainErrors.InvalidThreshold` | `ReportsTests.cs` (1-9), `ExtensionAndCurryingTests.cs` (10-14), `IntegrationTests.cs` (28-30) | sections 1-4, 9-11 |
| Anuar Gabdulin | IT-2513 | `native/budgetcalc.c`, `native/budgetcalc.h`, `native/build.sh`, `NativeMethods.cs` (P/Invoke), `NativeUsage.cs` (adapter), `NativeErrors.cs`, `MoneyExtensions.ToCents`, `Validation.Check` (`yield`), `CsvExport.cs`, `ReportFile.cs` (`using`, temporary file), `FileErrors.WriteFailed`, native copy settings in `BudgetTool.csproj` | `DeferredExecutionTests.cs` (15-17), `ResourceTests.cs` (18-21), `NativeTests.cs` (22-27) | sections 5-8 |
