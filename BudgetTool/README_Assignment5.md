# Budget Summary - Assignment 5: Composing Functions Using Pipelining

## 1. Project workflow

**Topic:** monthly budget summary. **Intended users:** people who want to see whether any spending category exceeds a limit set as a share of their income.

**Workflow:** a JSON file with transactions is read, every record is validated, valid transactions go through a three-stage pure pipeline, and a summary is printed. The same pipeline runs with two limit policies on the same data.

### Input (JSON)

```json
{
  "transactions": [
    { "id": "T1", "category": "salary", "amount": 1000, "kind": "income" }
  ]
}
```

| Field | Meaning | Rule |
|---|---|---|
| `id` | transaction identifier | not empty |
| `category` | spending or income category | not empty |
| `amount` | amount of money | number or numeric string, 0 or greater (0 is valid) |
| `kind` | `income` or `expense` | case-insensitive |

A missing `transactions` key, a root that is not an object, or invalid JSON syntax is a file-level error. `"transactions": []` is a valid empty dataset.

### Domain rules

- Category names are normalised: trimmed and lower-cased.
- Duplicate transaction ids are removed; the first record is kept.
- Income = sum of income transactions. Expenses = sum of expense transactions. Balance = Income - Expenses.
- The limit per expense category is calculated from total income by the selected policy:
  - **Moderate:** 30% of income.
  - **Strict:** 20% of income.
- A category is over the limit if `Spent > Limit`. Spent equal to the limit is **not** over (boundary rule).
- Empty dataset: all totals are 0 and there are no category lines.
- With income 0 the limit is 0, so any expense above 0 is over the limit.

### Expected output

Dataset `data/transactions.json` (expected results were written before the first run):

| Record | Expected handling |
|---|---|
| T1 salary 1000 income | accepted |
| T2 " Rent " 300 expense | accepted, category becomes `rent` |
| T3 food 150 expense | accepted |
| T4 food 50 expense | accepted, food total = 200 |
| T5 fun 90 expense | accepted |
| T3 food 150 expense (second) | accepted by validation, removed as duplicate id in stage 1 |
| T7 misc -5 expense | rejected: negative amount |
| T8 misc "abc" expense | rejected: not a number |

Accepted by validation: 6, rejected: 2. After stage 1: 5 transactions.
Income 1000, expenses 590, balance 410. Spent per category: rent 300, food 200, fun 90.

| Policy | Limit | rent (300) | food (200) | fun (90) | Categories over limit |
|---|---|---|---|---|---|
| Moderate | 300 | ok (boundary) | ok | ok | **0** |
| Strict | 200 | **OVER** | ok (boundary) | ok | **1** |

Console output of the normal run:

```
Input file: data/transactions.json
Accepted records: 6, rejected records: 2
  REJECTED - Record 7: T7: amount must not be negative (got -5)
  REJECTED - Record 8: T8: amount 'abc' is not a number

Policy: moderate
  Income: 1000.00  Expenses: 590.00  Balance: 410.00
  rent       spent=300.00 limit=300.00 ok
  food       spent=200.00 limit=300.00 ok
  fun        spent=90.00 limit=300.00 ok
  Categories over limit: 0

Policy: strict
  Income: 1000.00  Expenses: 590.00  Balance: 410.00
  rent       spent=300.00 limit=200.00 OVER
  food       spent=200.00 limit=200.00 ok
  fun        spent=90.00 limit=200.00 ok
  Categories over limit: 1
```

## 2. Build, run and test

- **.NET SDK:** 10.0 (check with `dotnet --version`).
- **Working directory:** the `BudgetTool` folder (it contains `BudgetTool.csproj` and `data`).
- **Prerequisites:** internet access on the first build to restore the xUnit NuGet packages.

```
cd BudgetTool
dotnet build BudgetTool.csproj
dotnet run -- --file data/transactions.json --policy both
dotnet test ../BudgetToolTests
```

Arguments: `--file <path to .json>` and `--policy moderate|strict|both` (default `both`).

### Demonstrations

**Normal run, both policies:**

```
dotnet run -- --file data/transactions.json --policy both
```

**One policy only:**

```
dotnet run -- --file data/transactions.json --policy strict
```

**Empty dataset** (`data/empty.json` contains `{ "transactions": [] }`): accepted 0, rejected 0, all totals 0.

```
dotnet run -- --file data/empty.json
```

**Failure demonstrations:**

| Command | Expected result |
|---|---|
| `dotnet run -- --file data/transactions.json` | records T7 and T8 are listed as REJECTED; valid records are still processed |
| `dotnet run -- --file data/invalid.json` (`data/invalid.json` contains broken JSON) | `Input error: Invalid JSON ...`, exit code 1, no result is produced |
| `dotnet run -- --file data/missing.json` | `Cannot read 'data/missing.json': ...`, exit code 1 |
| `dotnet run -- --file data/transactions.json --policy lenient` | `Unknown policy: lenient`, exit code 1 |
| `dotnet run -- --file data/transactions.txt` | `Unsupported file type: use .json`, exit code 1 |

## 3. Pipeline stages

The workflow is composed in one place, `Pipeline.Run`:

```csharp
BuildSummary(Evaluate(Normalize(input), limitFor))
```

| Stage | Input type -> output type | Rule | Example |
|---|---|---|---|
| 1 `Normalize` | `IEnumerable<Transaction>` -> `IReadOnlyList<Transaction>` | trim and lower-case the category; drop duplicate ids (first record is kept) | `" Rent "` becomes `rent`; the second `T3` is removed |
| 2 `Evaluate` | `IReadOnlyList<Transaction>` + `Func<decimal, decimal>` -> `Evaluation` | sum income and expenses; limit = `limitFor(income)`; Spent per category; `IsOver = Spent > Limit` | income 1000, Moderate limit 300, rent spent 300 -> not over |
| 3 `BuildSummary` | `Evaluation` -> `BudgetSummary` | Balance = Income - Expenses; lines sorted by Spent descending, then Category; count over-limit lines | balance 410, lines rent, food, fun |

### Trace of one record (T2)

1. JSON text `{ "id": "T2", "category": " Rent ", "amount": 300, "kind": "expense" }`
2. `RawTransaction("T2", " Rent ", "300", "expense")` (all text, nothing checked)
3. `Transaction("T2", "Rent", 300m, Expense)` (validated; `Validation` trims the category)
4. Stage 1: `Transaction("T2", "rent", 300m, Expense)` (new object, category normalised)
5. Stage 2: contributes 300 to expenses and to `CategoryLine("rent", 300, limit, IsOver)`
6. Stage 3: appears in `BudgetSummary.Lines` as the first line (largest spend)

### Models (immutable `sealed record`)

| Model | Purpose |
|---|---|
| `RawTransaction` | raw text fields as read from JSON (may be null or malformed) |
| `Transaction` | validated transaction (`decimal` amount, `TransactionKind` enum) |
| `CategoryLine` | one category: Spent, Limit, IsOver |
| `Evaluation` | result of stage 2: Income, Expenses, Lines |
| `BudgetSummary` | final result: adds Balance and OverLimitCount |
| `ParseOutcome` | valid transactions plus one message per rejected record |
| `RawReadResult` | raw records or a file-level format error |

Raw text and valid domain data are separate types. Collections stored in results are newly created lists that the pipeline does not share with the caller.

## 4. Configurable rule

`Pipeline.Evaluate` is the higher-order function. It receives `Func<decimal, decimal> limitFor` and calls it (`limitFor(income)`) during processing. The two policies are `Policies.Moderate` (30%) and `Policies.Strict` (20%). The policy is chosen in `Program.cs` from the `--policy` argument via `Policies.Get` and is passed as an argument to `Pipeline.Run`. There is no global or mutable setting. Running both policies on the same data gives 0 over-limit categories (Moderate) versus 1 (Strict) with identical pipeline code.

## 5. Functional core and I/O separation

- `Pipeline.cs`, `Policies.cs`, `Validation.cs`, `JsonInputParser.cs` and `Display.cs` do not use `Console`, files, the clock, random numbers or mutable static state.
- Stages do not modify their arguments: `Normalize` creates new `Transaction` objects with `with`, and each stage builds a new list.
- `Display.Format` only builds a string. `Program.cs` is the only file that reads files and writes to the console; reading happens before the computation and printing after it.
- Invalid values are never replaced by defaults, and the null-forgiving operator is not used. A `null` dataset throws `ArgumentNullException`; an empty dataset is a valid input.

## 6. Requirement map

| Requirement | Where |
|---|---|
| Define the workflow, dataset with accepted, rejected, boundary and policy-dependent records | this README, `data/transactions.json` |
| Typed immutable input and output models | `Models.cs` |
| Parse and validate at the boundary; separate raw text from domain data | `JsonInputParser.cs` (JSON to `RawTransaction`), `Validation.cs` (`TryParseTransaction`, `ParseAll`) |
| Missing input vs empty dataset vs zero | `Validation.ParseAll` (null throws, empty is valid), amount 0 accepted |
| At least three pure stages, composed | `Pipeline.cs`: `Normalize`, `Evaluate`, `BuildSummary`, composed in `Run` |
| Higher-order function with a delegate call | `Pipeline.Evaluate` (`limitFor(income)`) |
| Two policies selected outside the pipeline | `Policies.cs` (`Moderate`, `Strict`, `Get`), selection in `Program.cs` |
| Same results for same input and policy; no mutation | `Pipeline.cs`; tests `Run_IsDeterministic`, `Normalize_DoesNotModifyCallerList` |
| Display and file operations after computation | `Display.cs` (string only), `Program.cs` |
| Automated tests | `PipelineTests.cs` (BudgetTool.Tests project) |

## 7. Automated tests (xUnit)

| # | Test | Required case | Expected result |
|---|---|---|---|
| 1 | `Normal_Moderate_GivesDocumentedResult` | Normal workflow | income 1000, expenses 590, balance 410, 0 over; order rent, food, fun |
| 2 | `Normalize_TrimsCategory_AndDropsDuplicateId` | Normal workflow, stage check | 5 transactions, category `rent`, one `T3` |
| 3 | `EmptyDataset_GivesAllZeros` | Empty dataset | all totals 0, no lines |
| 4 | `EmptyJsonArray_IsValidEmptyDataset` | Empty dataset | no format error, no records, no errors |
| 5 | `SpentEqualToLimit_IsNotOver` | Boundary value | rent 300 (Moderate) and food 200 (Strict) are not over |
| 6 | `ZeroAmount_IsValid` | Boundary value | amount "0" is accepted |
| 7 | `ZeroIncome_AnyExpenseIsOver` | Boundary value | limit 0, expense 10 is over |
| 8 | `InvalidRecords_AreRejected_WithMessages` | Invalid and missing input | 7 bad records give 7 errors and 0 transactions |
| 9 | `JsonNull_AndNonObjectItem_AreTreatedAsMissing` | Invalid and missing input | null category and non-object item are rejected |
| 10 | `JsonNumberAndNumericString_GiveSameAmount` | Input parsing | `300` and `"300"` both give 300 |
| 11 | `BrokenJson_AndMissingTransactionsKey_GiveFormatError` | Invalid and missing input | format error, no fabricated result |
| 12 | `NullInput_Throws` | Invalid and missing input | `ArgumentNullException` for null text and null datasets |
| 13 | `TwoPolicies_SameInput_DifferentResults` | Two policies | Moderate 0 over, Strict 1 over (rent), same balance |
| 14 | `PolicyLookup_FindsKnown_AndRejectsUnknown` | Two policies | known names found, unknown and null give null |
| 15 | `Evaluate_InvokesDelegate` | Two policies / higher-order | delegate is called once, limit 100 on all lines |
| 16 | `Normalize_DoesNotModifyCallerList` | Purity and stages | caller list unchanged (6 items, `" Rent "` intact); later changes do not affect the result |
| 17 | `Run_IsDeterministic` | Purity and stages | two runs give equal results |

**Latest test outcome:** total: 17; failed: 0; succeeded:17; skipped:0;

## 8. Team and contributions

| Name | Group | Implemented logic | Tests | Documentation |
|---|---|---|---|---|
| Anuar Gabdulin | IT-2513 | Pipeline, models and JSON reading: `Pipeline.cs`, `Models.cs`, `JsonInputParser.cs` | 1-5,9-11,15-17 (normal workflow, empty dataset, boundary values, JSON parsing, purity) | Sections 1 (workflow, input and rules), 3 (stages, trace and models),4 (configurable rule), 5 |
| Arlan Narushev | IT-2513 | Validation, policies, output and program shell: `Validation.cs`, `Policies.cs`, `Display.cs`, `Program.cs` | 6-8,12-14 (invalid and missing input, two policies, purity) | Sections 2 (build, run and demonstrations), 6 (requirement map), 7 (tests) |

Shared: the dataset in `data/` and the expected results in section 1 were prepared together.
