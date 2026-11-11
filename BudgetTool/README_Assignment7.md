# Budget Summary - Assignment 7: Exception Handling in FP

This snapshot extends the Assignment 6 project. The pipeline (Assignment 5), the validated factory and the safe state transitions (Assignment 6) are still active and are now composed with `Result`, `Option`, `Map` and `Bind`. The earlier descriptions are kept for history in `README_Assignment5.md` and `README_Assignment6.md`; where they differ from this file (for example, `Confirmed` now also carries the policy name and `Program.cs` no longer pre-checks the policy name), this file is current.

## 1. Workflow, input and domain rules

**Operation:** produce a confirmed monthly budget report for a policy, optionally focused on one expense category.

```
JSON file --BudgetFile.Load--> BudgetData --ReportFlow.Run--> ReportView --Program.cs--> console
            (boundary, I/O)                 (pure Result chain)           (handled once)
```

**Input:** a JSON object with a `transactions` array (`id`, `category`, `amount`, `kind` = `income`/`expense`). Record-level validation from Assignment 5 is unchanged: invalid records are listed as `REJECTED` and the valid ones are used.

**Domain rules**

| Rule | Where |
|---|---|
| A budget needs at least one income transaction with an amount above 0 | `ValidatedBudget.Create` |
| The policy must be known: `moderate` (limit 30% of income) or `strict` (20%) | `ValidatedBudget.Create`, `Policies.Find` |
| A report must be evaluated before it is confirmed | `Workflow.Confirm` |
| A report can be confirmed only if no category is over its limit (spent equal to the limit is not over) | `Workflow.Confirm` |
| If a focus category is requested, it must exist in the report | `CategoryLookup.Require` |

**Exit codes:** `0` every report succeeded, `2` at least one report ended with a domain failure, `1` operational error (file problem) or wrong arguments.

## 2. Explicit domain outcomes (Task 1)

`Result<T>` (`Result.cs`) has exactly two cases:

| Case | Payload | Protection against contradictory states |
|---|---|---|
| `Success<T>` | `Value` | the constructor rejects `null`, so a success always has its value |
| `Failure<T>` | `Error` | the constructor rejects `null`; there is no `Value` property, so a failure cannot be read as a value |

The base record has a `private protected` constructor, so no other case can be added. `Result.Ok` and `Result.Fail` are the factory helpers.

`Error` (`Error.cs`) is a structured error: `Code`, `Message` and `Kind` (`Domain` or `Operational`). Empty codes or messages are rejected. Domain errors are built in `DomainErrors.cs`, file errors in `FileErrors.cs`. No business failure is signalled by throwing.

**Expected outcomes**

| Situation | Outcome | Code | Message (example) |
|---|---|---|---|
| Valid data, moderate policy | `Success<ReportView>` | - | report with `Status: Confirmed (moderate, balance 600.00)` |
| No income | `Failure` | `budget.no_income` | `at least one income transaction with an amount above 0 is required` |
| Unknown policy | `Failure` | `budget.unknown_policy` | `unknown policy 'lenient' (known: moderate, strict)` |
| Both rules above broken | `Failure` | `budget.invalid` | `2 budget rules broken: budget.no_income: ...; budget.unknown_policy: ...` |
| Confirm before evaluate | `Failure` | `report.not_evaluated` | `cannot confirm: the report has not been evaluated yet` |
| Category over limit | `Failure` | `report.over_limit` | `cannot confirm: categories over limit: 1 (fun: spent 400.00, limit 300.00)` |
| Requested category missing | `Failure` | `lookup.category_not_found` | `category 'travel' was not found in the report (available: fun, rent, food)` |
| Evaluate twice / unknown state | `Failure` | `report.already_evaluated`, `report.already_confirmed`, `report.unknown_state` | reachable through `ReportFlow.Evaluate` / `Confirm` |

Every error names the rule, policy or category involved; a failure never becomes an empty successful report.

## 3. Missing values (Task 2)

`Option<T>` (`Option.cs`) has two cases: `Some<T>` (carries a non-null value) and `None<T>` (no value).

**Real lookups**

| Lookup | Returns | Present | Absent |
|---|---|---|---|
| `CategoryLookup.Find(summary, name)` | `Option<CategoryLine>` | `Some(line)` | `None` |
| `CategoryLookup.Require(summary, name)` | `Result<CategoryLine>` | `Success(line)` | `Failure(lookup.category_not_found)` via `ToResult` |
| `Policies.Find(name)` | `Option<Policy>` | `Some(policy)` | `None`, turned into `budget.unknown_policy` by the factory |

- **Zero is not absence.** A category with `spent = 0.00` (`gifts` in `data/zero_category.json`) is `Some`, because absence is decided only by whether the category exists, never by its amount. `Find` loops over the lines and returns `None` explicitly; it does not use `FirstOrDefault`, `default(T)` or `null`. An empty string as a category name is also a real value: it is looked up and simply not found.
- **Optional vs required.** The `--category` argument itself is an `Option<string>`. `None` (no argument) is the optional case: the report is produced without a focus line. `Some(name)` makes the lookup required: a missing category stops the operation with `lookup.category_not_found`.

**What each type represents**

| Type | Meaning |
|---|---|
| `BudgetCreation` (`BudgetValid` / `BudgetInvalid`) | outcome of validating the input data (Assignment 6 factory); `BudgetInvalid` keeps every broken rule as an `Error` |
| `ReportState` (`Draft` / `Evaluated` / `Confirmed`) | where a valid report is in its life cycle; not a success or a failure |
| `TransitionResult` (`Moved` / `Refused`) | whether a state transition was allowed; `Refused` now carries an `Error` |
| `Result<T>` | the common success/failure type used to compose all steps into one chain |
| `Option<T>` | a value that may legitimately be absent |

`ReportFlow.CreateBudget`, `ReportFlow.Evaluate` and `ReportFlow.Confirm` convert the Assignment 6 cases into `Result` with pattern matching.

## 4. Map and Bind (Task 3)

```
Map  : Result<T> + Func<T, U>          -> Result<U>
Bind : Result<T> + Func<T, Result<U>>  -> Result<U>
```

Both are extension methods in `ResultExtensions` (`Result.cs`). On `Success` they call the function with the value; on `Failure` they return a new `Failure<U>` with the **same** `Error` and never call the function. The value is read only inside the `Success<T>` pattern.

**The project chain** (`ReportFlow.Run`):

```csharp
CreateBudget(items, policyName)                    // Result<ValidatedBudget>
    .Map(budget => Workflow.Start(budget))         // Result<ReportState>     Map: Start cannot fail
    .Bind(draft => Evaluate(draft))                // Result<Evaluated>       Bind: transition may be refused
    .Bind(evaluated => Confirm(evaluated))         // Result<Confirmed>       Bind: over limit is refused
    .Bind(confirmed => Focus(confirmed, category)) // Result<ConfirmedReport> Bind: lookup may fail
    .Map(report => ReportView.From(report));       // Result<ReportView>      Map: pure display model
```

- **Map** is used when the next step is a plain function that always succeeds (`Workflow.Start`, building `ReportView`).
- **Bind** is used when the next step itself returns `Result`. With `Map` it would give `Result<Result<T>>`; `Bind` flattens it.
- A missing income stops the chain at `CreateBudget`: `Start`, `Evaluate`, `Confirm` and the lookup are never called (proved by `ProjectChain_AfterFactoryFailure_SkipsEveryLaterStep`).
- Inside `Focus`, `Map` is also used after the required lookup to wrap the found line into the report.

The final `Result<ReportView>` is handled once in `Program.cs` with `switch` on `Success<ReportView>` / `Failure<ReportView>`.

## 5. Boundary handling (Task 4)

All file access is in `BudgetFile.cs`:

```csharp
BudgetFile.Load(path) =
    ReadText(path)                                     // Result<string>          (I/O)
        .Bind(text => JsonInputParser.Parse(text, path)) // Result<raw records>     (JSON)
        .Map(records => ToData(path, records));         // Result<BudgetData>      (validation)
```

| Problem | Caught | Outcome |
|---|---|---|
| extension is not `.json` | checked before reading | `file.unsupported_type` |
| file or folder does not exist | `FileNotFoundException`, `DirectoryNotFoundException` | `file.not_found` |
| no permission / other I/O failure | `UnauthorizedAccessException`, `IOException` | `file.unreadable` |
| invalid JSON or wrong shape | `JsonException` in `JsonInputParser.Read`, returned as `file.malformed` by `Parse` | `file.malformed` |

- Only these specific exceptions are caught, each becomes a `Failure` with `Kind = Operational` and the file path in the message. There is no empty catch and no catch-all.
- **No partial update:** the whole file is parsed before any data is produced. On failure the caller receives no `BudgetData` at all, so nothing half-loaded exists. All domain values are immutable, so data loaded earlier stays unchanged (asserted in `MissingFile_...` and `MalformedJson_...`). The program never writes files.
- There is no fallback: on a file error `Program.cs` prints the error, prints `No report was produced.` and exits with code 1.
- Domain failures (`Kind = Domain`) are printed as `DOMAIN FAILURE`, operational ones as `OPERATIONAL ERROR`.

## 6. Build, run and test

- **.NET SDK:** 10.0 (check with `dotnet --version`).
- **Working directory:** the `BudgetTool` folder (it contains `BudgetTool.csproj` and `data`).
- **Prerequisites:** internet access on the first build to restore the xUnit NuGet packages.

```
cd BudgetTool
dotnet build BudgetTool.csproj
dotnet run -- --file data/transactions.json --policy both --category food
dotnet test ../BudgetToolTests
```

Arguments: `--file <path to .json>` (default `data/transactions.json`), `--policy moderate|strict|both|<name>` (default `both`), optional `--category <name>`, optional `--confirm-first`.

## 7. Demonstrations (Task 5)

| Path | Command (from `BudgetTool`) | Result |
|---|---|---|
| Normal + lookup present | `dotnet run -- --policy both --category food` | moderate confirmed with focus `food`; strict `report.over_limit` |
| Domain failure: over limit | same command, `strict` section | `report.over_limit` |
| Domain failure: unknown policy | `dotnet run -- --policy lenient` | `budget.unknown_policy` |
| Domain failure: no income | `dotnet run -- --file data/no_income.json` | `budget.no_income` |
| Domain failure: wrong order | `dotnet run -- --confirm-first --policy moderate` | `report.not_evaluated` |
| Lookup missing | `dotnet run -- --category travel --policy moderate` | `lookup.category_not_found` |
| Lookup present, value 0 | `dotnet run -- --file data/zero_category.json --policy moderate --category gifts` | found, `spent=0.00` |
| File missing | `dotnet run -- --file data/missing.json` | `file.not_found`, exit code 1 |
| File malformed | `dotnet run -- --file data/invalid.json` | `file.malformed`, exit code 1 |

**Recorded output**

```
$ dotnet run -- --policy both --category food
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
  Focus category: food spent=200.00 limit=450.00 ok
  Status: Confirmed (moderate, balance 600.00)

== strict ==
DOMAIN FAILURE [report.over_limit]: cannot confirm: categories over limit: 1 (fun: spent 400.00, limit 300.00)
```

```
$ dotnet run -- --policy lenient
== lenient ==
DOMAIN FAILURE [budget.unknown_policy]: unknown policy 'lenient' (known: moderate, strict)

$ dotnet run -- --file data/no_income.json
== moderate ==
DOMAIN FAILURE [budget.no_income]: at least one income transaction with an amount above 0 is required

$ dotnet run -- --confirm-first --policy moderate
== moderate ==
DOMAIN FAILURE [report.not_evaluated]: cannot confirm: the report has not been evaluated yet

$ dotnet run -- --category travel --policy moderate
== moderate ==
DOMAIN FAILURE [lookup.category_not_found]: category 'travel' was not found in the report (available: fun, rent, food)
```

(The input lines printed before `==` are omitted above.)

```
$ dotnet run -- --file data/zero_category.json --policy moderate --category gifts
Input file: data/zero_category.json
Accepted records: 3, rejected records: 0

== moderate ==
Policy: moderate
  Income: 1000.00  Expenses: 120.00  Balance: 880.00
  food       spent=120.00 limit=300.00 ok
  gifts      spent=0.00 limit=300.00 ok
  Categories over limit: 0
  Focus category: gifts spent=0.00 limit=300.00 ok
  Status: Confirmed (moderate, balance 880.00)
```

```
$ dotnet run -- --file data/missing.json
OPERATIONAL ERROR [file.not_found]: data file 'data/missing.json' was not found
No report was produced.

$ dotnet run -- --file data/invalid.json
OPERATIONAL ERROR [file.malformed]: data file 'data/invalid.json' is malformed: Invalid JSON: '0' is an invalid end of a number. Expected a delimiter. LineNumber: 5 | BytePositionInLine: 19.
No report was produced.
```

No demonstrated case ends with an unhandled exception.

## 8. Requirement map

| Requirement | Files and methods |
|---|---|
| Result with distinct cases, protected against contradictions | `Result.cs` (`Result<T>`, `Success<T>`, `Failure<T>`, `Result.Ok/Fail`), `Error.cs` |
| At least three domain failures with useful errors | `DomainErrors.cs`; raised in `ValidatedBudget.Create`, `Workflow.Evaluate/Confirm`, `CategoryLookup.Require` |
| Missing values with Option, present and absent | `Option.cs`, `CategoryLookup.Find/Require`, `Policies.Find`, `--category` in `Program.cs` |
| Missing required item becomes a Result failure | `OptionExtensions.ToResult`, `CategoryLookup.Require` |
| Assignment 6 cases converted to Result | `ReportFlow.CreateBudget`, `ReportFlow.Evaluate`, `ReportFlow.Confirm` |
| Map and Bind in a real chain | `ResultExtensions.Map/Bind`; `ReportFlow.Run`, `ReportFlow.ConfirmWithoutEvaluation`, `ReportFlow.Focus`, `BudgetFile.Load` |
| Final outcome handled once at the boundary | `Program.cs` (`switch` on `Success`/`Failure`) |
| File and parsing exceptions caught at the boundary | `BudgetFile.ReadText`, `JsonInputParser.Read/Parse`, `FileErrors.cs` |
| Earlier pipeline and safe transitions kept | `Pipeline.Run` (via `Workflow.Evaluate`), `Workflow`, `ReportState.cs` |
| Pure core, I/O isolated | I/O only in `BudgetFile.ReadText` and `Program.cs` |

## 9. Automated tests (xUnit)

33 earlier tests (17 in `PipelineTests.cs`, 16 in `WorkflowTests.cs`) still pass unchanged, plus 23 new tests: **56 cumulative**.

| # | Test | Required case | Expected result |
|---|---|---|---|
| | **`ResultTests.cs`** | | |
| 1 | `Map_Success_PassesValue_AndReturnsNewValue` | Map success | function receives 21, result is `Success(42)` |
| 2 | `Bind_Success_PassesValue_AndKeepsTheNextResult` | Bind success | function receives 5, result `Success("value 5")`; a failure returned by the next step is the same object |
| 3 | `MapAndBind_OnFailure_DoNotCallTheFunction_AndKeepTheError` | Failure propagation | counter stays 0, both results carry the same `Error` |
| 4 | `ProjectChain_AfterFactoryFailure_SkipsEveryLaterStep` | Failure propagation | real chain with no income: counter stays 0, `budget.no_income` |
| 5 | `Constructors_RejectContradictoryStates` | Model protection | `Success(null)`, `Failure(null)`, `Some(null)`, empty error code/message, `BudgetInvalid` without errors all throw |
| 6 | `Option_ZeroFalseAndEmptyString_ArePresentValues` | Lookup present/absent | `Some(0)`, `Some(false)`, `Some("")` are present; `None` becomes the given failure |
| | **`ReportFlowTests.cs`** | | |
| 7 | `CompleteSuccess_Moderate_WithFocusCategory` | Complete success | balance 410, 0 over, focus `food` spent 200 limit 300, status Confirmed |
| 8 | `OptionalCategory_NotGiven_ReportHasNoFocus` | Optional lookup | success, focus is `None`, no focus line |
| 9 | `DomainFailure_NoIncome_IsReportedWithCode` | Domain failure | `budget.no_income`, kind Domain |
| 10 | `DomainFailure_UnknownPolicy_NamesThePolicy` | Domain failure | `budget.unknown_policy`, message names `lenient` |
| 11 | `DomainFailure_OverLimit_NamesTheCategory` | Domain failure | `report.over_limit`, names `rent`, not `food` (equal to limit) |
| 12 | `DomainFailure_ConfirmBeforeEvaluate_IsRejectedByState` | Domain failure | `report.not_evaluated` |
| 13 | `DomainFailure_MissingRequiredCategory_NamesItAndListsAvailable` | Domain failure / lookup absent | `lookup.category_not_found`, names `travel`, lists `rent` |
| 14 | `DomainFailure_SeveralBrokenRules_AreReportedTogether` | Domain failure | `budget.invalid` containing both codes |
| 15 | `Lookup_PresentCategory_IsFound_IgnoringCaseAndSpaces` | Lookup present | `" RENT "` finds `rent`, spent 300 |
| 16 | `Lookup_ZeroSpentCategory_IsPresent_NotAbsent` | Lookup present, primitive 0 | `gifts` with spent 0 is `Some` and `Success` |
| 17 | `Lookup_UnknownCategory_IsNone_AndRequiredLookupFails` | Lookup absent | `travel` and `""` are `None`; `Require` fails with `lookup.category_not_found` |
| | **`BoundaryTests.cs`** (own temporary folder per test, deleted in `Dispose`) | | |
| 18 | `ValidFile_LoadsAcceptedAndRejectedRecords` | Boundary success | 2 transactions, 1 rejected record (negative amount) |
| 19 | `MissingFile_IsReported_AndPreviousDataStaysUnchanged` | Missing file | `file.not_found`, kind Operational, path in message; earlier data still has 2 transactions; no file created |
| 20 | `MissingDirectory_IsReportedAsNotFound` | Missing file | `file.not_found`, folder name in message |
| 21 | `MalformedJson_BecomesFailure_WithoutPartialData` | Malformed file | `file.malformed`, no `BudgetData` produced, earlier data unchanged, file content unchanged |
| 22 | `WrongJsonShape_IsMalformed` | Malformed file | object without `transactions` gives `file.malformed` |
| 23 | `UnsupportedExtension_IsRejectedBeforeReading` | Boundary | `.csv` gives `file.unsupported_type` |

**Latest test outcome:** total: 56; failed: 0; succeeded: 56; skipped: 0.

## 10. Team and contributions

| Name | Group | Implemented logic | Tests | Documentation |
|---|---|---|---|---|
| Arlan Narushev | IT-2513 | `Result.cs` (`Result<T>`, `Map`, `Bind`), `Option.cs` (`Option<T>`, `ToResult`), `ReportFlow.cs` (Result chain, conversion of Assignment 6 cases, `ReportView`), `CategoryLookup.cs`; changes in `ReportState.cs` (`Refused` carries `Error`, `Confirmed.PolicyName`), `Workflow.cs` (error codes), `Program.cs` (`--category`, single outcome handler); `data/zero_category.json` | `ResultTests.cs` (1-6), `ReportFlowTests.cs` (7-17) | sections 2 (Result), 3, 4, 8, 9 |
| Anuar Gabdulin | IT-2513 | `Error.cs`, `DomainErrors.cs`, `FileErrors.cs`, `BudgetFile.cs` (exception boundary), `JsonInputParser.Parse`; changes in `ValidatedBudget.cs` (`BudgetInvalid` with `Error` list), `Policies.cs` (`Find` returns `Option<Policy>`), `Display.cs` (`DescribeError`, `DescribeLine`) | `BoundaryTests.cs` (18-23) | sections 1, 2 (errors table), 5, 6, 7 |
