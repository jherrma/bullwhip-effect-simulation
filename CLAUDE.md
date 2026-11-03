# CLAUDE.md — working rules for this repository

This is the simulation behind a German bachelor thesis on the bullwhip effect. Every formula, constant and
test expectation is cited in the thesis, so behaviour must stay reproducible and every change must be traceable.

## Build and test

- Build: `dotnet build --no-incremental` (0 warnings, 0 errors required; `TreatWarningsAsErrors` is on).
- Test: `dotnet test`. All tests must pass. Never change a test expectation to make a test pass; report the failure instead.
- Hash baselines: refactors must leave every result hash unchanged. If a hash changes, revert the change or stop and report.
  Intentional behaviour changes need a DECISIONS entry in the thesis folder, not just a commit message.

## Structure rules (enforced by `tests/Bullwhip.Tests/StructureRulesTests.cs`)

1. **Method length:** at most 50 lines per method, constructor, local function or property accessor body (counted from the opening to the closing brace, both included).
2. **Nesting depth:** at most 4 nested blocks of control flow (`if`, `else if`, `for`, `foreach`, `while`, `do`, `switch`, `try`, `using`, `lock`) inside one method. Count the block that contains the statement; a flat `if` chain is one level per `else if` branch, not a new level.
   - Reduce nesting with guard clauses (early `return`/`continue`), extracted methods, or a lookup table.
   - Do not add `// NOLINT`-style escapes. If an exception is truly needed, add it to the explicit allow-list in `StructureRulesTests.cs` with a reason and mention it in the commit message.
3. Keep the rule list short. Do not add rules without a real failure that motivated them.

## Code conventions

- Names: descriptive, no abbreviations for thesis-facing terms. Use the thesis notation only where it is defined (for example `mu`, `sigma`, `L`, `R`, `p`, `z`) and cite the source in XML `<remarks>` on the method that uses it.
- Comments say what the code does now. Do not write change history ("unchanged", "as before", "was 0.886") into the code; that belongs in DECISIONS.md or commit messages.
- Derivations and citations go in `<remarks>` next to the formula, not in the loop body.
- Keep `Bullwhip.Core` free of I/O. Keep test helpers in one place and do not duplicate them across test files.
- Independence: the V1 check (`ChenSingleStageCheck`) must not use the chain's forecaster (`StageForecast`/`MovingAverage`). Keep its sampling and estimator path separate.

## Scope and git

- Do not commit or push unless the user asks. Commit messages end with `Co-Authored-By: Claude Haiku 5.5 <noreply@anthropic.com>` when a commit is requested.
- Do not edit the thesis documents in the Obsidian vault from this repository; those are maintained in the thesis folder.
- Memory and decisions for the thesis go to the thesis DECISIONS.md, not to local markdown memory files.

## Where things are

- `src/Bullwhip.Core/Chain/SerialChain.cs`: the multi-stage chain simulation (period loop, orders, measurement).
- `src/Bullwhip.Core/Chain/CoverRules.cs`, `PhaseTarget.cs`: cover rules for R > 1 and real-valued lead times.
- `src/Bullwhip.Core/Validation/GateA.cs`, `ChenSingleStageCheck.cs`: validation checks against Chen et al. (2000) and Kaplan (1970).
- `src/Bullwhip.Core/Analytic/*Formulas.cs`: closed-form expressions with their citations.
- `README.md` is partly outdated (it describes milestone M1); trust the code and the tests.
