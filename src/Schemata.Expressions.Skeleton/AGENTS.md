# Schemata.Expressions.Skeleton

## OVERVIEW

Contracts and shared plumbing for the Expressions family: filter/order compilers, pushdown planning, language resolution, compile caching. One file covers all four packages; the siblings get none of their own:

- [../Schemata.Expressions.Aip](../Schemata.Expressions.Aip): AIP-160 filter language (35 files).
- [../Schemata.Expressions.Cel](../Schemata.Expressions.Cel): CEL filter language (24 files, ~2.9k LOC).
- [../Schemata.Expressions.Order](../Schemata.Expressions.Order): AIP-132 order-by compiler (2 files).

Siblings reference only Skeleton + Common and are independent of each other. No `src/` csproj references Aip, Cel or Order: the integrating modules (Resource, Insight, Flow) depend on Skeleton only, and consumers pick a language package via a meta target or an explicit `PackageReference`.

## CONTRACTS

- [IExpressionCompiler.cs](IExpressionCompiler.cs): `Language`, `Parse`, `Compile<TContext,TResult>`.
- [IExpressionPushdownPlanner.cs](IExpressionPushdownPlanner.cs): `Language`, `Plan`.
- [IOrderCompiler.cs](IOrderCompiler.cs): `CompileOrder<T>`, `Parse`.
- [IExpressionTree.cs](IExpressionTree.cs): `Language`; implemented by Aip `Filter` and Cel `CelNode`.
- [IExpressionLanguageBuilder.cs](IExpressionLanguageBuilder.cs): `Services`, `Languages`.

## SUPPORTING TYPES

- `ExpressionLanguageDescriptor` (Language, Filtering, MaxResidualScanRows, SupportsValues=false); `ExpressionLanguageProfile` + `ExpressionLanguageEntry` (Enable) in [ExpressionLanguageOptions.cs](ExpressionLanguageOptions.cs); `ExpressionLanguageResolver.Resolve(profile, requested, descriptors)` -> `ResolvedLanguage`; `ExpressionLanguages.Aip`="aip" / `.Cel`="cel".
- `ExpressionPushdownPlan` (Pushed, Residual); `ExpressionCapabilities` with the `Relational` preset (Comparison, Logical, Presence, Wildcard, Arithmetic, Membership, StringMatch); `FilteringMode` {Default, Strict, Residual} plus `Narrow`/`OrStrict`.
- `ExpressionCache` + `ExpressionCacheKey` (SHA-256 over language, source, contextType, resultType, options); `ExpressionCompileOptions.Functions` + `Fingerprint`; `ExpressionFunction`; `ExpressionException`; `UnknownExpressionLanguageException`; `DynamicValues` (alias-keyed row helpers, `Missing` sentinel); `OrderKey` (Path, Descending); `ResidualPage.ScanAsync`.

## KEYING RULE

The single most important fact: compilers, pushdown planners and descriptors are ALL registered with `AddKeyedSingleton` keyed by the language id string ("aip" / "cel"). Plain `AddSingleton` is invisible to `GetRequiredKeyedService`. `IOrderCompiler` is the exception: NON-KEYED, registered once via `TryAddSingleton` in [../Schemata.Expressions.Order/ServiceCollectionExtensions.cs](../Schemata.Expressions.Order/ServiceCollectionExtensions.cs).

## RESOLUTION

`ExpressionLanguageResolver` picks the first enabled language when the request omits one, otherwise requires an ordinal match. Effective `Filtering` is the `Narrow` (intersection) of descriptor, profile and entry: Strict wins, then Residual, and an all-Default chain becomes Strict. `MaxResidualScanRows` takes the first positive of entry, profile, descriptor, then the 10_000 default.

## DI ENTRY POINTS

- Aip: `AddAipExpressions` / `UseAip<T>`; registers `SupportsValues=false` (predicate-only).
- Cel: `AddCelExpressions` / `UseCel<T>`; registers `SupportsValues=true`.
- Order: `AddOrderExpressions` / `UseOrdering<T>`; does NOT add a language to the profile.

`UseAip` / `UseCel` also call `builder.Languages.Enable(...)` on the module profile.

## PUSHDOWN vs RESIDUAL

`Plan(tree, ExpressionCapabilities.Relational)` returns (Pushed, Residual). `Pushed` is a WEAKENING: every row the original keeps, Pushed keeps, and `Pushed AND Residual` is equivalent to the original. Pushdown runs ONLY in `FilteringMode.Residual`; Strict compiles the whole tree. Both planners rebuild split sub-trees whose `Source` gets a `U+0001` sentinel suffix (`P` for pushed, `R` for residual) so the two halves land on distinct compile-cache keys. The residual runs through `ResidualPage.ScanAsync`, which THROWS `InvalidOperationException` rather than materialize an unbounded superset when the cap is reached. Planners are conservative by construction: unprovable pushability falls to the residual.

- Aip ([../Schemata.Expressions.Aip/AipPushdownPlanner.cs](../Schemata.Expressions.Aip/AipPushdownPlanner.cs)): splits only at top-level AND; a conjunct pushes only when both sides are flat fields (no navigation chain) with the matching capability flags. Navigation chains stay residual because AIP null-chain guard semantics can diverge from SQL three-valued logic. `field:*` is a presence test, not a glob.
- Cel ([../Schemata.Expressions.Cel/CelPushdownPlanner.cs](../Schemata.Expressions.Cel/CelPushdownPlanner.cs)): flattens top-level `&&` only when Logical is available, otherwise degrades to whole-or-nothing. `CelMember`, `matches`, macros (exists/all/filter/map), conditionals, list/map literals and indexes are ALWAYS residual.

## CONSUMERS

- Resource.Foundation resolves the compiler and planner keyed by the resolved language, the order compiler non-keyed: [../Schemata.Resource.Foundation/ResourceOperationHandler.List.cs](../Schemata.Resource.Foundation/ResourceOperationHandler.List.cs).
- Insight.Foundation does the same.
- Flow.Foundation `ProcessRegistry` resolves `GetKeyedService<IExpressionCompiler>(configuration.Language)` at PROCESS-REGISTRATION time and raises FLOW_EXPRESSION_LANGUAGE_REQUIRED / FLOW_EXPRESSION_LANGUAGE_NOT_REGISTERED. A definition registered without a compiler leaves its string conditions uncompiled.

## ORDER

[../Schemata.Expressions.Order/OrderCompiler.cs](../Schemata.Expressions.Order/OrderCompiler.cs) is the ONLY `IOrderCompiler` in the repo. Parses AIP-132 (`field [asc|desc], ...`) into `OrderKey`; resolves path segments through `Schemata.Common` MemberAccess.

## CEL CONFORMANCE

[../../tests/Schemata.Expressions.Cel.Tests/Conformance/CelSpecLoader.cs](../../tests/Schemata.Expressions.Cel.Tests/Conformance/CelSpecLoader.cs) reads `../../../../../specs/cel/tests/simple/testdata/{suite}.textproto` via a hard-coded relative path (no MSBuild item), across suites basic, comparisons, integer_math, fp_math, logic, lists, string, conversions, macros, macros2, timestamps; out-of-scope cases are excluded by `cel-spec-skips.txt`. The `specs/cel` submodule must be initialized.

## GOTCHAS

- AIP-160 INNER WILDCARDS are rejected: `A*B` is not a supported simple wildcard. [../Schemata.Expressions.Aip/AipCompileVisitor.cs](../Schemata.Expressions.Aip/AipCompileVisitor.cs) trims leading/trailing stars, detects a remaining `*`, and falls back to plain literal equality on the trimmed pattern, so `name = 'A*B'` silently compares against the literal `A*B` (the inner star is kept). Only leading-only, trailing-only and both-ends wildcards translate to StartsWith / EndsWith / Contains.
- CEL ships NO `IOrderCompiler`, so a CEL module still needs `UseOrdering()` for AIP-132 order-by.
- Registering a compiler with `AddSingleton` instead of `AddKeyedSingleton` makes it undiscoverable.
- A custom language needs compiler, descriptor, planner and profile entry all under the SAME key.
- CEL `matches` runs `Regex.IsMatch` with a 100 ms timeout.

## STALE DOCS

`docs/documents/resource/read-pipeline.md` claims `IOrderCompiler` is resolved keyed by `AipLanguage.Name` and that `IExpressionCompiler` is fixed to AIP. Both are wrong: the list handler resolves the compiler keyed by the resolved language and the order compiler non-keyed (see CONSUMERS). Mentioned here only; the doc itself is not corrected from this file.
