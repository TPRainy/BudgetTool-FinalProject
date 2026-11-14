# BudgetTool — Final Project

Introduction to Functional Programming, academic year 2026–2027.

BudgetTool is a C# console application that analyses a monthly budget. It loads transactions from JSON, rejects invalid records with a reason, checks spending per category against a limit set by a configurable policy, moves a report through explicit states (Draft → Evaluated → Confirmed), builds two LINQ reports with a usage percentage computed by a native C library, and exports a report to CSV.

| | |
|---|---|
| Team | Arlan Narushev (IT-2513), Anuar Gabdulin (IT-2513) |
| Repository | https://github.com/TPRainy/BudgetTool-FinalProject |
| Branch | `main` |
| Final commit | `df668d2` |
| .NET SDK | 10.0 (tested on the 64-bit .NET 10.0.12 runtime) |
| Platform | Windows x64 (a Linux x64 native binary is also included) |

## Contents

1. [Problem, users and scope](#1-problem-users-and-scope)
2. [Build, run and test](#2-build-run-and-test)
3. [Workflows](#3-workflows)
4. [Architecture](#4-architecture)
5. [Domain rules and outcomes](#5-domain-rules-and-outcomes)
6. [Functional techniques](#6-functional-techniques)
7. [Persistence and resources](#7-persistence-and-resources)
8. [Native C library](#8-native-c-library)
9. [Requirement map](#9-requirement-map)
10. [Automated tests](#10-automated-tests)
11. [Demonstrations](#11-demonstrations)
12. [Team responsibilities](#12-team-responsibilities)
13. [Limitations](#13-limitations)

## 1. Problem, users and scope

**Problem.** A person managing a monthly budget needs to know whether spending in each category stays within a sensible share of income, which categories use most of their limit, and which single payments are large.

**Intended users.** One person (or a reviewer) checking a month of income and expenses.

**Goals.**

- Reject invalid data with a clear reason instead of silently fixing it.
- Apply one of two limit policies without changing the calculation code.
- Allow a report to be confirmed only after it has been evaluated and only if no category is over its limit.
- Report every expected failure explicitly, without unhandled exceptions.

**Implemented scope.** Two policies (`moderate` = 30% of income per category, `strict` = 20%), a three-state report life cycle with an optional focus category, two LINQ reports, a native usage calculation in C, CSV export, and 86 automated tests.

**Domain models.** `RawTransaction` (as read from the file), `Transaction` (validated), `CategoryLine`, `Evaluation`, `BudgetSummary`, `ValidatedBudget`, the report states `Draft` / `Evaluated` / `Confirmed`, and the report rows `SpendingRow`, `CategoryUsageRow`, `LargeExpenseRow`.

## 2. Build, run and test

Prerequisites: .NET SDK 10.0 and internet access on the first build (to restore the xUnit packages). No C compiler is needed to run the project: the native binary is included.

Working directory for all commands: the `BudgetTool` folder (it contains `BudgetTool.csproj`, `data` and `native`).

```
cd BudgetTool
dotnet build BudgetTool.csproj
dotnet test ../BudgetToolTests
dotnet run -- --policy both --export category_report.csv
```

**Arguments**

| Argument | Default | Meaning |
|---|---|---|
| `--file <json>` | `data/transactions.json` | input file (relative to the working directory) |
| `--policy moderate\|strict\|both\|<name>` | `both` | policy to apply; an unknown name gives `budget.unknown_policy` |
| `--category <name>` | none | focus category; a missing category gives `lookup.category_not_found` |
| `--confirm-first` | off | demonstrates the invalid order: confirm without evaluating |
| `--min <amount>` | `100` | minimum amount (inclusive) for the large-expense report |
| `--export <file.csv>` | none | export the category report to CSV |

**Exit codes:** `0` everything succeeded; `2` at least one domain failure; `1` an operational error (file, export, native library) or invalid arguments.

**Sample data** (`BudgetTool/data/`)

| File | Purpose |
|---|---|
| `transactions.json` | normal data: 6 valid records, 2 invalid (negative amount, non-numeric amount), one duplicated id |
| `no_income.json` | domain failure: no income |
| `zero_category.json` | a category with spending 0.00 (present, not missing) |
| `empty.json` | valid empty dataset |
| `invalid.json` | malformed JSON |

## 3. Workflows

| # | Workflow | Input | Business rules | Result |
|---|---|---|---|---|
| 1 | Load and validate data | JSON file | file must exist and be valid JSON; each record needs id, category, a non-negative numeric amount and kind `income`/`expense` | `BudgetData` snapshot with accepted transactions and rejection messages, or `file.not_found` / `file.malformed` |
| 2 | Report life cycle | transactions, policy, optional focus category | income above 0; known policy; evaluate before confirm; confirm only if no category is over its limit; focus category must exist | confirmed report view, or a domain failure with a code |
| 3 | Analysis | transactions, policies, minimum amount | minimum amount not negative; usage computed in C from amounts in cents | two LINQ reports per run |
| 4 | Export | analysis result, target path | target must be a `.csv` file in an existing folder; a failed write keeps the previous file | CSV file, or `file.write_failed` / `file.unsupported_type` |

**Data flow**

```
JSON file -> BudgetFile.Load (boundary) -> BudgetData snapshot
   |-- per policy: ReportFlow.Run
   |      CreateBudget -Map-> Start -Bind-> Evaluate -Bind-> Confirm -Bind-> Focus -Map-> ReportView
   |-- Analysis.Run: Normalize once -> Report 1 per policy (+ native usage) and Report 2
   '-- optional: CsvExport.CategoryLines (lazy) -> ReportFile.Save (using + temporary file)
Program.cs handles every Result once, prints it and sets the exit code.
```

**Expected result for `transactions.json`:** 6 accepted, 2 rejected; after removing the duplicate: income 1500.00, expenses 900.00, balance 600.00. Moderate limit 450.00 → 0 categories over limit → confirmed. Strict limit 300.00 → `fun` (400.00) is over the limit → `report.over_limit`.

## 4. Architecture

```
BudgetTool/
  Program.cs     imperative shell: arguments, console output, exit codes
  Domain/        immutable models and outcome types
                 Models, Error, Result, Option, ReportState, ValidatedBudget, DomainErrors
  Core/          pure business logic
                 Pipeline, Validation, Policies, Workflow, ReportFlow, CategoryLookup, MoneyExtensions
  Reports/       LINQ reports and text/CSV formatting (pure)
                 Reports, Analysis, CsvExport, Display
  IO/            file boundary: JSON input, CSV output, file errors
                 BudgetFile, JsonInputParser, ReportFile, FileErrors
  Interop/       C# side of the native library
                 NativeMethods, NativeUsage, NativeErrors
  native/        C source, header, build script, prebuilt binaries (runtimes/<rid>/)
  data/          sample input files
BudgetToolTests/ xUnit tests, same folder layout
  Domain/  Core/  Reports/  IO/  Interop/  Integration/
```

All classes are in the `BudgetTool` namespace (tests in `BudgetTool.Tests`); the folders group files by layer.

**Pure logic and side effects.** `Domain`, `Core` and `Reports` contain no console, file, clock or global mutable state: every function takes values and returns new values, so it is tested without files or console. Side effects are only in `Program.cs` (console, exit code), `IO` (file system) and `Interop` (native call). Exceptions from these boundaries are converted into `Result` failures where they happen.

## 5. Domain rules and outcomes

| Situation | Outcome | Code | Kind |
|---|---|---|---|
| Valid data, moderate policy | `Success<ReportView>` | — | — |
| No income above 0 | `Failure` | `budget.no_income` | Domain |
| Unknown policy | `Failure` | `budget.unknown_policy` | Domain |
| Both rules above broken | `Failure` | `budget.invalid` | Domain |
| Confirm before evaluate | `Failure` | `report.not_evaluated` | Domain |
| A category is over its limit | `Failure` | `report.over_limit` | Domain |
| Evaluate twice / unknown state | `Failure` | `report.already_evaluated`, `report.already_confirmed`, `report.unknown_state` | Domain |
| Requested focus category missing | `Failure` | `lookup.category_not_found` | Domain |
| Negative `--min` | `Failure` | `report.invalid_threshold` | Domain |
| Amount outside the native range | `Failure` | `usage.invalid_input`, `usage.rejected` | Domain |
| File missing / unreadable / wrong extension | `Failure` | `file.not_found`, `file.unreadable`, `file.unsupported_type` | Operational |
| Invalid JSON or wrong shape | `Failure` | `file.malformed` | Operational |
| CSV cannot be written | `Failure` | `file.write_failed` | Operational |
| Native library or export missing | `Failure` | `native.unavailable` | Operational |

Invalid records are not failures of the whole run: they are listed as `REJECTED` with a reason and do not reach the calculations. "Over limit" means strictly greater: spending equal to the limit is allowed.

## 6. Functional techniques

### Pure pipeline and configurable behaviour

`Pipeline.Run` composes three named stages, each output feeding the next:

| Stage | Input → output | Rule |
|---|---|---|
| `Normalize` | transactions → transactions | trim and lower-case categories (`with`), keep the first record per id |
| `Evaluate` | transactions + `Func<decimal, decimal>` → `Evaluation` | totals; invokes the policy to get the category limit; marks `spent > limit` |
| `BuildSummary` | `Evaluation` → `BudgetSummary` | orders lines (total descending, then name), computes balance and over-limit count |

The policy is a higher-order input: `Pipeline.Evaluate` invokes the supplied `Func<decimal, decimal>` (`var limit = limitFor(income)`). The policy is selected outside the pipeline and passed as a value; there is no mutable global setting.

### Currying and partial application

```csharp
public static Func<decimal, decimal> LimitShare(decimal share) => income => income * share;
public static Func<decimal, decimal> Moderate { get; } = LimitShare(0.30m);
public static Func<decimal, decimal> Strict   { get; } = LimitShare(0.20m);
```

On the same data (income 1500) Moderate gives a limit of 450 and confirms the report; Strict gives 300 and refuses it.

### Immutable models and pattern matching

All models are `sealed record` types with get-only properties; updates use `with`. The validated budget copies the incoming collection and exposes it read-only, so caller-owned collections are never changed. Pattern matching makes the domain decision in `Workflow.Confirm`:

```csharp
Evaluated e when e.Summary.OverLimitCount == 0 => new Moved(new Confirmed(e.Budget.PolicyName, e.Summary)),
Evaluated e => new Refused(DomainErrors.OverLimit(e.Summary.Lines.Where(l => l.IsOver))),
Confirmed c => new Moved(c),
Draft       => new Refused(DomainErrors.NotEvaluated()),
_           => new Refused(DomainErrors.UnknownState())
```

### Safe operation sequence

The hidden dependency this design removes (documentation only, not in the project):

```csharp
public class UnsafeBudgetReport
{
    public List<Transaction> Items = new();
    public Func<decimal, decimal>? LimitFor;
    public BudgetSummary? Summary;
    public bool Confirmed;

    public void SetPolicy(string name) { LimitFor = Policies.Get(name); }
    public void Evaluate()             { Summary = Pipeline.Run(Items, LimitFor); }
    public void Confirm()              { Confirmed = true; }
}
```

Here `Confirm()` compiles and succeeds without `Evaluate()`, producing a confirmed report with no numbers.

The safe design:

- **Factory.** `ValidatedBudget.Create` checks income and policy and returns `BudgetValid` or `BudgetInvalid` (with every broken rule). The constructor is private, so the checks cannot be bypassed.
- **State types.** `Draft`, `Evaluated` and `Confirmed` are sealed records with internal constructors; each carries only its own data.
- **Transitions.** `Workflow.Start` accepts only a `ValidatedBudget`. `Evaluate` and `Confirm` return `Moved(next)` or `Refused(error)` and never modify the input state.

| State | `Evaluate` | `Confirm` |
|---|---|---|
| Draft | Moved → Evaluated | Refused `report.not_evaluated` |
| Evaluated, nothing over limit | Refused `report.already_evaluated` | Moved → Confirmed |
| Evaluated, some category over limit | Refused `report.already_evaluated` | Refused `report.over_limit` |
| Confirmed | Refused `report.already_confirmed` | Moved → the same Confirmed (idempotent) |

The invalid order is rejected at run time with a clear error (`dotnet run -- --confirm-first`); the legitimate order (evaluate, then confirm) is preserved.

### Functional error handling

`Result<T>` has two sealed cases, `Success<T>(Value)` and `Failure<T>(Error)`; constructors reject a null value or error. `Error` carries `Code`, `Message` and `Kind` (`Domain` or `Operational`).

```
Map  : Result<T> + Func<T, U>          -> Result<U>
Bind : Result<T> + Func<T, Result<U>>  -> Result<U>
```

On failure both skip the function and pass the same error on. The main chain (`ReportFlow.Run`):

```csharp
CreateBudget(items, policyName)                    // Result<ValidatedBudget>
    .Map(budget => Workflow.Start(budget))         // Result<ReportState>
    .Bind(draft => Evaluate(draft))                // Result<Evaluated>
    .Bind(evaluated => Confirm(evaluated))         // Result<Confirmed>
    .Bind(confirmed => Focus(confirmed, category)) // Result<ConfirmedReport>
    .Map(report => ReportView.From(report));       // Result<ReportView>
```

A failure in `CreateBudget` skips every later step (asserted with a counter in `ProjectChain_AfterFactoryFailure_SkipsEveryLaterStep`). The final result is handled once in `Program.cs` with pattern matching.

**Missing lookups.** `Option<T>` has `Some<T>` and `None<T>`. `CategoryLookup.Find` returns `Option<CategoryLine>`; `Require` converts `None` into `lookup.category_not_found` with `ToResult`. A category that spent 0.00 is `Some`: absence depends only on whether the category exists. `Policies.Find` also returns `Option<Policy>`.

### Extension methods and LINQ reports

| Extension | Receiver → result | Used in |
|---|---|---|
| `MoneyExtensions.ToCents` | `decimal` → `long` cents, rounded half away from zero | `NativeUsage.Percent` |
| `ResultExtensions.Sequence` | `IEnumerable<Result<T>>` → `Result<IReadOnlyList<T>>`, stops at the first failure | `Analysis.Run`, `Analysis.AddUsage` |

`Map`, `Bind` and `ToResult` are also extension methods.

| Report | Query stages | Result on `transactions.json` |
|---|---|---|
| `Reports.SpendingByCategory` | `Where(expense)` → `GroupBy(category)` → `Select` with `Count`, `Sum`, share → `OrderByDescending(total)` → `ThenBy(category, Ordinal)` → `ToList` | fun 400.00 (44.44%), rent 300.00 (33.33%), food 200.00 (22.22%); usage moderate 89/67/44%, strict 133/100/67% |
| `Reports.LargeExpenses(min)` | `Where(expense)` → `Where(amount >= min)` → `OrderByDescending(amount)` → `ThenBy(id, Ordinal)` → `Select` → `ToList`; `Sum` for the total | T5 400.00, T2 300.00, T3 150.00; total 850.00 at min 100 |

`ThenBy` makes the order deterministic when primary values tie. Both reports are pure; `Display` and `CsvExport` format them, `ReportFile` writes them.

### Lazy processing

`Validation.Check` is a `yield return` iterator producing `AcceptedRecord` / `RejectedRecord`. Creating it validates nothing; each `MoveNext` validates one record. `Validation.ParseAll` materialises it once with `ToList()`; the resulting snapshot is reused by every policy, both reports and the export. `CsvExport.CategoryLines` is deliberately streamed into the open writer instead. Tests use a counting probe source, so production code has no diagnostic counters.

## 7. Persistence and resources

**Loading** (`IO/BudgetFile.cs`):

```csharp
BudgetFile.Load(path) =
    ReadText(path)                                      // Result<string>        (I/O)
        .Bind(text => JsonInputParser.Parse(text, path)) // Result<raw records>   (JSON)
        .Map(records => ToData(path, records));          // Result<BudgetData>    (validation)
```

| Problem | Caught | Outcome |
|---|---|---|
| extension is not `.json` | checked before reading | `file.unsupported_type` |
| file or folder does not exist | `FileNotFoundException`, `DirectoryNotFoundException` | `file.not_found` |
| no permission / other I/O failure | `UnauthorizedAccessException`, `IOException` | `file.unreadable` |
| invalid JSON or wrong shape | `JsonException` | `file.malformed` |

Only specific exceptions are caught; there is no empty catch and no catch-all. The whole file is parsed before any data is produced, so a failure leaves no partial data.

**Saving** (`IO/ReportFile.cs`): lines are written to `<target>.tmp` inside two `using` blocks (`FileStream`, `StreamWriter`) and moved over the target only after the writer is closed; the temporary file is deleted in `finally`. A failure never leaves a half-written report and keeps any previous export.

| Resource | Owner | Released |
|---|---|---|
| input file handle | `File.ReadAllText` (convenience API) | before it returns, on every path |
| `JsonDocument` | `JsonInputParser.Read` (`using`) | end of `Read`; records are copied with `ToList()` before disposal |
| `FileStream` + `StreamWriter` | `ReportFile.Save` (`using`) | end of the `using` blocks, before the move |
| native library | .NET runtime | process exit |

**Retention.** Datasets are materialised in `JsonInputParser.Read`, `Validation.ParseAll`, `Pipeline.Normalize` (once per analysis) and the report rows. No long-lived collections, caches or event subscriptions are used. `Analysis.Run` normalises the transactions once and shares the list between all reports.

## 8. Native C library

| | |
|---|---|
| Function | `int64_t bt_usage_percent(int64_t spent_cents, int64_t limit_cents)` (`native/budgetcalc.h`, `cdecl`) |
| Result | whole percent of the limit used, rounded half up: `(spent_cents * 100 + limit_cents / 2) / limit_cents` |
| Valid range | `spent_cents` 0 to 10^14; `limit_cents` 1 to 10^14; otherwise returns `-1` |
| Manually checked example | 400.00 of 300.00 → 40 000 × 100 + 15 000 = 4 015 000; / 30 000 = 133 |
| C# binding | `NativeMethods.UsagePercent` (`DllImport("budgetcalc", EntryPoint = "bt_usage_percent", CallingConvention = Cdecl)`) |
| Adapter | `NativeUsage.Percent`: validates the amounts, converts with `ToCents`, calls C, turns `DllNotFoundException`, `EntryPointNotFoundException`, `BadImageFormatException` into `native.unavailable` |
| Used in | `Analysis.AddUsage` → `usage` column of report 1 and of the CSV |
| Prebuilt binaries | `native/runtimes/win-x64/budgetcalc.dll` (clang 18.1.3 + lld-link), `native/runtimes/linux-x64/libbudgetcalc.so` (gcc 13.3.0) |
| Rebuild | `bash native/build.sh win-x64` or `bash native/build.sh linux-x64` from the `BudgetTool` folder |
| Loading | `BudgetTool.csproj` copies the binary for the current OS next to `BudgetTool.dll` on every build |

**Integration difficulty.** No MSVC or MinGW toolchain was available, so the DLL was cross-compiled with clang and lld-link as a freestanding library (`-ffreestanding -nostdlib -Wl,-noentry`). This works because the function uses only integer arithmetic; the result was checked with `llvm-readobj` (one export, no imports).

## 9. Requirement map

| Requirement | Files and methods |
|---|---|
| I/O separated from domain logic | `Program.cs`, `IO/`, `Interop/` vs pure `Domain/`, `Core/`, `Reports/` |
| Immutable core models, caller collections unchanged | `Domain/Models.cs` (sealed records), `Pipeline.Normalize` (`with`), `ValidatedBudget.Create` (copy + read-only) |
| Pattern matching for a domain decision | `Workflow.Confirm`, `Workflow.Evaluate`, `ReportFlow.CreateBudget` |
| Pipeline with three named stages | `Pipeline.Normalize` → `Pipeline.Evaluate` → `Pipeline.BuildSummary` |
| Higher-order function changing a business rule | `Pipeline.Evaluate(items, Func<decimal, decimal> limitFor)`; `Policies.Moderate` / `Strict` |
| Safe operation sequence | `ValidatedBudget.Create`, `Domain/ReportState.cs`, `Workflow.Start` / `Evaluate` / `Confirm`; demo `--confirm-first` |
| Result for at least three domain failures | `Domain/Result.cs`; `budget.no_income`, `budget.unknown_policy`, `report.over_limit`, `report.not_evaluated`, `lookup.category_not_found`, `report.invalid_threshold` |
| Map and Bind in a real workflow, failure skips later stages | `ReportFlow.Run`, `BudgetFile.Load`, `Analysis.BuildCategoryReport` |
| Missing lookups as Option | `Domain/Option.cs`, `CategoryLookup.Find` / `Require`, `Policies.Find` |
| Boundary exceptions → clear outcomes | `BudgetFile.ReadText`, `JsonInputParser.Read`, `ReportFile.Save`, `NativeUsage.Percent` |
| Two custom extension methods used | `MoneyExtensions.ToCents`, `ResultExtensions.Sequence` |
| Two LINQ reports (filter, projection, ordering with tie-breaker, grouping, aggregation) | `Reports.SpendingByCategory`, `Reports.LargeExpenses` |
| `yield return`, deferred execution, deliberate materialisation | `Validation.Check` / `ParseAll`, `CsvExport.CategoryLines` |
| Load from JSON, save/export, sample files, relative paths | `BudgetFile.Load`, `ReportFile.Save`, `data/`, `--file` / `--export` |
| Missing file and malformed content handled | `file.not_found`, `file.malformed` (`IO/BoundaryTests.cs`) |
| Curried function with two partial applications | `Policies.LimitShare`, `Moderate`, `Strict` |
| Native C library through P/Invoke | `native/budgetcalc.c`, `Interop/NativeMethods.cs`, `Interop/NativeUsage.cs` |
| At least 12 automated tests | 86 xUnit tests in `BudgetToolTests/` |

## 10. Automated tests

**Latest outcome:** 86 total, 86 passed, 0 failed, 0 skipped (`dotnet test ../BudgetToolTests`, Windows, .NET 10).

| File | Tests | Covers |
|---|---|---|
| `Core/PipelineTests.cs` | 17 | normal workflow, empty dataset, boundary (spent = limit), zero amount, invalid and missing input, JSON parsing, two policies, purity (caller list unchanged), determinism |
| `Core/WorkflowTests.cs` | 16 | factory success and rejection, income boundary, distinct cases, normal operation, confirm before evaluate, repeated operations, limit boundary, state preservation, null state |
| `Domain/ResultTests.cs` | 6 | Map and Bind on success, failure propagation without calling the function (counter), project chain short-circuit, contradictory states, zero/false/empty as present values |
| `Core/ReportFlowTests.cs` | 11 | complete success, optional focus, each domain failure with its code, several rules at once, lookup present / zero / absent |
| `IO/BoundaryTests.cs` | 6 | valid file, **missing file**, missing folder, **malformed JSON** without partial data, wrong JSON shape, wrong extension |
| `Reports/ReportsTests.cs` | 9 | grouping and totals, ties, empty input, no matches, inclusive minimum, zero amounts, invalid records, reused failures |
| `Core/ExtensionAndCurryingTests.cs` | 5 | `ToCents`, `Sequence` (order, early stop), curried policy, different results on the same data |
| `Core/DeferredExecutionTests.cs` | 3 | no processing before enumeration, processing during iteration, materialisation and reuse |
| `IO/ResourceTests.cs` | 4 | disposal on success and failure, missing folder, input file released |
| `Interop/NativeTests.cs` | 6 | real native call against manual values, adapter validation, real load failure, missing export |
| `Integration/IntegrationTests.cs` | 3 | **complete workflow** JSON → reports with native call → CSV; native library unavailable; malformed input stops before reports |

Pure functions and domain validation: 67 tests (`Core`, `Domain`, `Reports`); complete workflows: 3 integration tests plus the end-to-end `ReportFlow` tests; file handling: 10 tests (`BoundaryTests`, `ResourceTests`); native integration: 6 tests. File tests use their own temporary folders, deleted after each test.

## 11. Demonstrations

| Case | Command (from `BudgetTool`) | Result | Exit |
|---|---|---|---|
| Successful workflow | `dotnet run -- --policy moderate` | `Status: Confirmed (moderate, balance 600.00)`, both reports | 0 |
| Two policies, expected failure | `dotnet run -- --policy both` | moderate confirmed; strict: `report.over_limit` | 2 |
| Lookup present | `dotnet run -- --policy moderate --category food` | `Focus category: food spent=200.00 limit=450.00 ok` | 0 |
| Lookup absent | `dotnet run -- --policy moderate --category travel` | `lookup.category_not_found` | 2 |
| Zero is not missing | `dotnet run -- --file data/zero_category.json --category gifts` | `Focus category: gifts spent=0.00 limit=300.00 ok` | 0 |
| Invalid order | `dotnet run -- --confirm-first` | `report.not_evaluated` | 2 |
| No income | `dotnet run -- --file data/no_income.json` | `budget.no_income` | 2 |
| Unknown policy | `dotnet run -- --policy lenient` | `budget.unknown_policy` | 2 |
| Missing file | `dotnet run -- --file data/missing.json` | `file.not_found`, no report | 1 |
| Malformed file | `dotnet run -- --file data/invalid.json` | `file.malformed`, no report | 1 |
| No matching records | `dotnet run -- --policy moderate --min 1000` | `no expenses at or above 1000.00` | 0 |
| Invalid threshold | `dotnet run -- --min -5` | `report.invalid_threshold`, nothing exported | 2 |
| Export | `dotnet run -- --policy both --export category_report.csv` | CSV with header + 6 rows | 2 |
| Export failure | `dotnet run -- --export missing_folder/report.csv` | `file.write_failed`, nothing created | 1 |

Recorded output of the normal run:

```
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
```

Exported `category_report.csv`:

```
policy,category,count,total,share_percent,limit,usage_percent
moderate,fun,1,400.00,44.44,450.00,89
moderate,rent,1,300.00,33.33,450.00,67
moderate,food,2,200.00,22.22,450.00,44
strict,fun,1,400.00,44.44,300.00,133
strict,rent,1,300.00,33.33,300.00,100
strict,food,2,200.00,22.22,300.00,67
```

## 12. Team responsibilities

| Feature / module | Owner |
|---|---|
| Record validation (`Core/Validation.cs`: `TryParseTransaction`, `ParseAll`) | Arlan Narushev |
| Policies, currying (`Core/Policies.cs`: `LimitShare`, `Moderate`, `Strict`) | Arlan Narushev |
| Report states and transitions (`Domain/ReportState.cs`, `Core/Workflow.cs`) | Arlan Narushev |
| `Result`, `Option`, `Map`, `Bind`, `Sequence`, `ToResult` (`Domain/Result.cs`, `Domain/Option.cs`) | Arlan Narushev |
| Result chain and lookup (`Core/ReportFlow.cs`, `Core/CategoryLookup.cs`) | Arlan Narushev |
| LINQ reports and analysis (`Reports/Reports.cs`, `Reports/Analysis.cs`) | Arlan Narushev |
| Console shell (`Program.cs`), report formatting (`Display.Format`, `FormatCategoryReport`, `FormatLargeExpenses`) | Arlan Narushev |
| Pipeline stages (`Core/Pipeline.cs`), models (`Domain/Models.cs`) | Anuar Gabdulin |
| Validated budget factory (`Domain/ValidatedBudget.cs`), state descriptions (`Display.DescribeState`, `DescribeResult`) | Anuar Gabdulin |
| Error model (`Domain/Error.cs`, `Domain/DomainErrors.cs`, `IO/FileErrors.cs`) | Anuar Gabdulin |
| File boundary (`IO/BudgetFile.cs`, `IO/JsonInputParser.cs`, `IO/ReportFile.cs`) | Anuar Gabdulin |
| Lazy validation (`Validation.Check`), CSV export (`Reports/CsvExport.cs`) | Anuar Gabdulin |
| Native C library and interop (`native/`, `Interop/`), `MoneyExtensions.ToCents` | Anuar Gabdulin |
| Folder structure by layer | Anuar Gabdulin |
| Sample data, README | shared |

| Tests | Owner |
|---|---|
| `ResultTests`, `ReportFlowTests`, `ReportsTests`, `ExtensionAndCurryingTests`, `IntegrationTests`; part of `PipelineTests` (invalid input, policies) and `WorkflowTests` (transitions) | Arlan Narushev |
| `BoundaryTests`, `DeferredExecutionTests`, `ResourceTests`, `NativeTests`; part of `PipelineTests` (pipeline, JSON) and `WorkflowTests` (factory) | Anuar Gabdulin |

## 13. Limitations

- Confirming a `Draft` is rejected at run time, not prevented at compile time, because `Workflow.Confirm` accepts the base type `ReportState`.
- Validation reports only the first broken rule of each record.
- With a very small income (for example 0.01) the category limit is below one cent and the analysis fails with `usage.invalid_input`.
- `Policies.Get` (returns `null` for an unknown name) is kept from Assignment 5 for older tests; the application uses `Policies.Find`.
- `Program.cs` output is checked manually; automated tests cover the pure functions, boundaries and workflows.
- Only two policies and one input format (JSON) are supported; budgets for several months are not.
