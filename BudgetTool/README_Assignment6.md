# Budget Summary - Assignment 6: Handling Temporal Coupling

This snapshot extends the Assignment 5 project. The pipeline, validation, policies and the 17 earlier tests are unchanged and still count toward the cumulative minimum. The Assignment 5 description is kept in `README_Assignment5.md`.

## 1. Workflow and domain rules

**Operation:** closing a monthly budget report. A report is built from validated transactions and a limit policy, evaluated by the existing pipeline, and then confirmed.

Domain rules added in this stage:

- A budget can only be created if there is at least one income transaction with an amount above 0, and the policy name is known (`moderate` or `strict`).
- A report can be confirmed only after it was evaluated, and only if no expense category is over its limit. Spent equal to the limit is not over.
- Order is legitimate: Draft -> Evaluated -> Confirmed. The order is enforced by the types, not by the caller's memory.

Input and the limit rules are the same as in Assignment 5 (`data/transactions.json`, Moderate 30%, Strict 20% of income).

## 2. The hidden dependency (unsafe "before" example)

This code is **documentation only**; it is not part of the project. It is the shape of the design that hides the order of calls:

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

Prerequisite: `SetPolicy` and a non-empty `Items` list before `Evaluate`; `Evaluate` before `Confirm`.

| Call sequence | What goes wrong |
|---|---|
| `Confirm()` without `Evaluate()` | `Confirmed = true` while `Summary` is null: a confirmed report with no numbers |
| `Evaluate()` without `SetPolicy()` | `LimitFor` is null, so the pipeline throws `ArgumentNullException` at runtime |
| `Items` has no income | every limit is 0 and every expense is reported as over limit: a nonsense result with no warning |
| `Items` changed after `Evaluate()` | the stored `Summary` is stale but can still be confirmed |
| `Confirm()` while a category is over its limit | accepted silently |
| `Confirm()` twice | behaviour is undefined |

## 3. The safe design ("after")

```
raw JSON -> Validation.ParseAll -> Transaction list
                                      |
                  ValidatedBudget.Create(list, policyName)
                      |                         |
               BudgetInvalid(reasons)     BudgetValid(ValidatedBudget)
                                                |
                                       Workflow.Start -> Draft
                       Workflow.Evaluate:  Draft -> Evaluated   (runs Pipeline.Run)
                       Workflow.Confirm:   Evaluated -> Confirmed (only if nothing is over limit)
```

Each transition is a pure function that takes a state and returns `Moved(nextState)` or `Refused(reason)`. The input state is never modified.

### Call sites

Unsafe: `report.Confirm();` (compiles, order is hidden).
Safe: `Workflow.Confirm(state)` where `state` is a `ReportState`. The only way to obtain an `Evaluated` state is `Workflow.Evaluate(draft)`, and the only way to obtain a `Draft` is `Workflow.Start(validatedBudget)`. Confirming a `Draft` compiles but is rejected explicitly with the reason `cannot confirm: the report has not been evaluated yet`.

## 4. Validated construction (Task 2)

`ValidatedBudget.Create(items, policyName)` in `ValidatedBudget.cs` returns a `BudgetCreation`, never a half-built object.

| Check | Rejection reason |
|---|---|
| at least one income transaction with amount above 0 | `at least one income transaction with an amount above 0 is required` |
| policy name is known | `unknown policy '<name>'` |

All broken rules are reported together. A `null` item list throws `ArgumentNullException` (missing input).

**Access restrictions**

- `ValidatedBudget` is a sealed class with a **private constructor** and get-only properties: it cannot be created with `new`, with an object initializer or with `with`.
- The factory copies the input into a new list and stores it as a `ReadOnlyCollection`, so later changes to the caller's list do not affect the budget and the stored collection cannot be cast back to a mutable `List`.
- `Draft`, `Evaluated` and `Confirmed` have **internal constructors** and get-only properties. Code outside the project can only obtain them through `Workflow`, so an `Evaluated` state with a forged summary cannot be built.
- The case hierarchies (`BudgetCreation`, `ReportState`, `TransitionResult`) have a `private protected` base constructor, so no other assembly can add a case.

The factory is used by the real workflow: `Program.cs` calls `ValidatedBudget.Create` for every policy, and `Workflow.Evaluate` reads the policy and transactions from the validated value.

## 5. Distinct cases (Task 3)

| Model | Case | Payload | Meaning |
|---|---|---|---|
| `BudgetCreation` | `BudgetValid` | `ValidatedBudget` | all rules passed |
| | `BudgetInvalid` | `IReadOnlyList<string> Reasons` | at least one rule failed |
| `ReportState` | `Draft` | `ValidatedBudget` | created, not evaluated |
| | `Evaluated` | `ValidatedBudget`, `BudgetSummary` | pipeline ran |
| | `Confirmed` | `BudgetSummary` | accepted, final |
| `TransitionResult` | `Moved` | next `ReportState` | transition allowed |
| | `Refused` | `string Reason` | transition rejected |

Each case carries only its own data: `BudgetInvalid` has no budget, `Draft` has no summary, `Confirmed` does not carry the transactions.

## 6. Pattern matching (Task 4)

- `Workflow.Evaluate` and `Workflow.Confirm` are switch expressions over `ReportState`; every case is listed and the fallback `_` returns `Refused("unknown or missing state")`, so `null` or an unexpected case is never treated as valid.
- `Program.cs` uses `switch (creation)` on `BudgetValid` / `BudgetInvalid` and property patterns (`Moved { Next: Evaluated evaluated }`) to decide what happens next.
- `Display.DescribeState` and `Display.DescribeResult` match all cases to produce the output text.
- No casts and no null-forgiving operator are used.

## 7. Safe operations (Task 5)

| Operation | Allowed from | Result | Otherwise |
|---|---|---|---|
| `Workflow.Start(budget)` | a `ValidatedBudget` | `Draft` | `null` budget throws `ArgumentNullException` |
| `Workflow.Evaluate(state)` | `Draft` | `Moved(Evaluated)` | `Refused` (already evaluated / already confirmed / unknown) |
| `Workflow.Confirm(state)` | `Evaluated` with 0 over-limit categories | `Moved(Confirmed)` | `Refused` (not evaluated yet / over limit / unknown) |

**Repeated operations (documented rules)**

- `Confirm` on `Confirmed`: idempotent, returns `Moved` with the **same** `Confirmed` state.
- `Evaluate` on `Evaluated` or `Confirmed`: refused.
- Creating a budget twice from the same data is allowed and gives two independent values.

**Consistency:** transitions never modify the state they receive and `ValidatedBudget` copies its input. A refused transition or a rejected factory call therefore leaves all existing objects and the caller's collections unchanged. Validation of all prerequisites happens before a new state is built.

**Separation:** `Workflow`, `ValidatedBudget`, `ReportState` and `Pipeline` do no I/O. Reading files and printing happen only in `Program.cs`.

## 8. Build, run and test

- **.NET SDK:** 10.0 (check with `dotnet --version`).
- **Working directory:** the `BudgetTool` folder (it contains `BudgetTool.csproj` and `data`).
- **Prerequisites:** internet access on the first build to restore the xUnit NuGet packages.

```
cd BudgetTool
dotnet build BudgetTool.csproj
dotnet run -- --file data/transactions.json --policy both
dotnet test ../BudgetToolTests
```

Arguments: `--file <path to .json>`, `--policy moderate|strict|both` (default `both`), optional `--confirm-first`.

### Demonstrations

| Command | What it shows |
|---|---|
| `dotnet run -- --file data/transactions.json --policy both` | valid operation (moderate) and a refused confirmation (strict, one category over limit); repeated confirmation |
| `dotnet run -- --file data/transactions.json --policy moderate --confirm-first` | invalid sequence: confirm before evaluate is refused |
| `dotnet run -- --file data/no_income.json --policy both` | factory rejection: no income |
| `dotnet run -- --file data/empty.json` | empty dataset: the factory rejects it (no income) |
| `dotnet run -- --file data/invalid.json` | file-level error, exit code 1 |

Expected output of the normal run (first lines show the input validation from Assignment 5):

```
Input file: data/transactions.json
Accepted records: 6, rejected records: 2
  REJECTED - Record 7: T7: amount must not be negative (got -5)
  REJECTED - Record 8: T8: amount 'abc' is not a number

== moderate ==
  Factory: valid (moderate, 6 transactions)
  Start: Draft (moderate, 6 transactions, not evaluated)
  Evaluate: OK -> Evaluated (moderate, balance 600.00, 0 over limit)
Policy: moderate
  Income: 1500.00  Expenses: 900.00  Balance: 600.00
  fun        spent=400.00 limit=450.00 ok
  rent       spent=300.00 limit=450.00 ok
  food       spent=200.00 limit=450.00 ok
  Categories over limit: 0
  Confirm: OK -> Confirmed (balance 600.00)
  Confirm again: OK -> Confirmed (balance 600.00)

== strict ==
  Factory: valid (strict, 6 transactions)
  Start: Draft (strict, 6 transactions, not evaluated)
  Evaluate: OK -> Evaluated (strict, balance 600.00, 1 over limit)
Policy: strict
  Income: 1500.00  Expenses: 900.00  Balance: 600.00
  fun        spent=400.00 limit=300.00 OVER
  rent       spent=300.00 limit=300.00 ok
  food       spent=200.00 limit=300.00 ok
  Categories over limit: 1
  Confirm: REFUSED: cannot confirm: categories over limit: 1
```

With `--confirm-first` (moderate) an extra line appears after `Start`:
`  Confirm before evaluate: REFUSED: cannot confirm: the report has not been evaluated yet`

With `data/no_income.json` each policy prints:

```
== moderate ==
  Factory: REJECTED
    - at least one income transaction with an amount above 0 is required
```

## 9. Requirement map

| Requirement | Where |
|---|---|
| Hidden dependency, unsafe example, what goes wrong | section 2 of this README (documentation only) |
| Factory with at least two checks, used by the application | `ValidatedBudget.Create` (`ValidatedBudget.cs`), called in `Program.cs` |
| No unchecked construction | private constructor, internal state constructors, `private protected` base constructors (section 4) |
| Distinct cases with their own payload | `BudgetCreation`, `ReportState`, `TransitionResult` (`ValidatedBudget.cs`, `ReportState.cs`) |
| Pattern matching in the real path | `Workflow.Evaluate`, `Workflow.Confirm`, `Program.cs`, `Display.DescribeState/DescribeResult` |
| Safe operations, explicit prerequisites | `Workflow.cs` (transitions take a state and return a new state or a refusal) |
| Success, two invalid cases, repeated operation | section 8 demonstrations and tests 9-13 |
| Pure core, I/O isolated | `Workflow.cs`, `ValidatedBudget.cs`, `ReportState.cs`, `Pipeline.cs`; I/O only in `Program.cs` |

## 10. Automated tests (xUnit)

17 tests from Assignment 5 (`PipelineTests.cs`) plus 16 new tests in `WorkflowTests.cs`: 33 cumulative.

| # | Test (`WorkflowTests.cs`) | Required case | Expected result |
|---|---|---|---|
| 1 | `Factory_ValidInput_CreatesUsableBudget` | Factory success | policy `moderate`, 6 transactions, `LimitFor(1000)` = 300, collection is not a `List` |
| 2 | `Factory_NoIncome_IsRejected_WithReason` | Factory rejection | `BudgetInvalid` with an income reason |
| 3 | `Factory_IncomeBoundary_ZeroRejected_SmallestPositiveAccepted` | Boundary value | income 0 rejected, 0.01 accepted |
| 4 | `Factory_UnknownOrMissingPolicy_IsRejected_WithReason` | Factory rejection | reason names the policy; `null` policy rejected |
| 5 | `Factory_ReportsEveryBrokenRule` | Factory rejection | two broken rules give two reasons |
| 6 | `Factory_NullItems_Throws` | Missing input | `ArgumentNullException` |
| 7 | `DistinctCases_AreReachable_AndMatchedExplicitly` | Distinct cases | every case is reached and described by pattern matching |
| 8 | `Factory_CopiesInput_AndFailureLeavesCallerDataUnchanged` | State preservation | later list changes do not leak in; failed creation leaves the caller list equal to its snapshot |
| 9 | `NormalOperation_Moderate_EvaluateThenConfirm` | Normal operation | balance 410, 0 over, confirmed with the same summary |
| 10 | `MissingPrerequisite_ConfirmBeforeEvaluate_IsRefused` | Missing prerequisite | `Refused` with reason "evaluated" |
| 11 | `OverLimit_Strict_ConfirmIsRefused` | Missing prerequisite | 1 over limit, confirmation refused |
| 12 | `Repeated_Confirm_ReturnsTheSameConfirmedState` | Repeated operation | second confirm returns the same state object |
| 13 | `Repeated_Evaluate_IsRefused` | Repeated operation | evaluate on `Evaluated` and `Confirmed` is refused |
| 14 | `Boundary_ExpenseEqualToLimit_CanBeConfirmed` | Boundary value | spent 200 with limit 200 moves; 200.01 is refused |
| 15 | `StatePreservation_FailedTransition_LeavesStatesUnchanged` | State preservation | failed confirms leave draft and evaluated state unchanged |
| 16 | `NullState_IsRefused_NotTreatedAsValid` | Missing prerequisite | `null` state gives `Refused` |

**Latest test outcome:** Test summary: total: 33; failed: 0; succeeded: 33; skipped: 0;

## 11. Team and contributions

| Name | Group | Implemented logic | Tests | Documentation |
|---|---|---|---|---|
| Anuar Gabdulin | IT-2513 | `ValidatedBudget.cs` (factory, `BudgetCreation`), `Display.cs` (`DescribeState`, `DescribeResult`) | 1-8 | sections 1-4 |
| Arlan Narushev | IT-2513 | `ReportState.cs`, `Workflow.cs`, `Program.cs`, `data/no_income.json` | 9-16 | sections 5-10 |