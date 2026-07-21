# SCHEMATA KNOWLEDGE BASE

**Generated:** 2026-07-10 · **Commit:** a696e55c (dirty) · **Branch:** master

## OVERVIEW

Modular .NET application framework shipped as ~60 NuGet packages (C#, `net8.0;net10.0`, ASP.NET Core). Consumers bootstrap via `WebApplicationBuilder.UseSchemata(...)`; features compose through a three-phase lifecycle (`ConfigureServices` / `ConfigureApplication` / `ConfigureEndpoints` on `ISimpleFeature`). Built with the dotnet Arcade SDK. There is no `Program.cs` in this repo — the entire public API is extension methods living in `Microsoft.AspNetCore.Builder` / `Microsoft.Extensions.DependencyInjection` namespaces.

## STRUCTURE

```
src/         60 packages: <Domain>.<Layer> naming (taxonomy below)
generators/  Roslyn source generators (netstandard2.0): Advice codegen + SKM DSL
targets/     10 MSBuild meta-packages (Application/Business/Module × Bare/Persisting/Complex[/Modular])
tests/       30 xUnit projects; classes named *Should, never *Tests
eng/         Arcade build infra — eng/common/ is generated upstream, DO NOT EDIT
docs/        DocFX site (guides/, cookbook/, documents/, modeling/, rfcs/) + cog CHANGELOG
specs/       git submodules: google/cel-spec, bpmn-miwg-test-suite (required by tests)
artifacts/   Arcade build drop; targets/ packages pack analyzer DLLs from here post-build
```

### Layer taxonomy (`<Domain>.<Layer>`)

| Suffix | Role |
|---|---|
| bare names (`Abstractions`, `Common`, `Core`, `Advice`, `Modular`) | framework kernel |
| `.Skeleton` | contracts: entities, options, advisor interfaces — no DI wiring |
| `.Foundation` | concrete implementation + `UseXxx()` feature registration |
| `.Http` / `.Grpc` | transport adapters |
| `.<Provider>` | vendor adapters: EntityFrameworkCore, LinqToDB, Redis, RabbitMq, AutoMapper, Mapster, FluentValidation, Identity |
| bridges | two-Foundation bridges: Flow.Event, Flow.Scheduling, Scheduling.Event, Push.Scheduling |
| engines | Flow.StateMachine (default, BPMN subset) vs Flow.Bpmn (full BPMN 2.0.2; in NO meta-target) |

## WHERE TO LOOK

| Task | Location |
|---|---|
| Host bootstrap, feature lifecycle, built-in `UseXxx` | `src/Schemata.Core` (`SchemataBuilder`, `SchemataStartup`, `Features/ISimpleFeature.cs`) |
| Cross-package contracts, `IAdvisor`, `SchemataConstants` | `src/Schemata.Abstractions` |
| Advisor pipeline runtime | `src/Schemata.Advice` + `generators/Schemata.Advice.Generator` |
| Repository/UoW + mutation advisors | `src/Schemata.Entity.Repository` (own AGENTS.md) |
| AIP-compliant CRUD resources | `src/Schemata.Resource.Foundation` (own AGENTS.md) + `.Http` / `.Grpc` |
| OAuth 2.0 / OIDC server | `src/Schemata.Authorization.Foundation` (own AGENTS.md) |
| ASP.NET Identity integration | `src/Schemata.Identity.{Skeleton,Foundation}` |
| Workflow AST, engine contracts, process builders | `src/Schemata.Flow.Skeleton` (own AGENTS.md) |
| BPMN 2.0.2 engine | `src/Schemata.Flow.Bpmn` (own AGENTS.md) |
| Filter/expression languages (CEL, AIP-160, order-by) | `src/Schemata.Expressions.{Skeleton,Cel,Aip,Order}` |
| SKM DSL → C# entity codegen | `generators/Schemata.Modeling.Generator` (own AGENTS.md) |
| Meta-package opt-in flags (`UseMapster`, `UseTenancy`, …) | `targets/*/Directory.Build.props` |
| Feature-level pitfalls | "Common pitfalls" sections in `docs/documents/**`, `docs/cookbook/**` |

## CODE MAP

Dependency backbone (bottom-up): `Abstractions` (zero deps) → `Common` → `Advice` → `Core` → `Modular`. `Entity.Repository` (deps: Advice, Common, Validation.Skeleton) underpins Identity/Authorization Skeletons and Resource.Foundation. Every `*.Foundation` depends on its own Skeleton + `Schemata.Core`.

**Advisor pattern** (framework-wide extension point): `IAdvisor<T1..T16>.AdviseAsync(AdviceContext, …) → Continue | Block | Handle`. Resolved via `GetServices<TAdvisor>().OrderBy(a => a.Order)`, short-circuits at the first non-`Continue`. Invoked as `Advisor.For<TAdvisor>().RunAsync(...)` — the `RunAsync` overloads are emitted by `Schemata.Advice.Generator` (attached as analyzer to every `src/*` project).

## CONVENTIONS (deviations from stock .NET only)

- `LangVersion=preview`, `Nullable=enable`, `ImplicitUsings=false` (explicit usings everywhere). No `.editorconfig`.
- Project behavior is determined by disk location: root `Directory.Build.props` switches `IsPackable`/`SignAssembly`/analyzers on `src/`, `tests/`, `generators/`, `targets/` path segments.
- ConfigureAwait.Fody weaves `ConfigureAwait(false)` into every await in `src/*` — never write it manually.
- Generated `.resx` accessors are rewritten `internal` → `public` by the `OverrideResourcesVisibility` target.
- Central package versions: `Directory.Packages.props` (separate net8.0/net10.0 blocks) + `eng/Versions.props`.
- `Order` sequences `ConfigureServices`; `Priority` sequences the app/endpoint pipeline. Range `[100_000_000, 900_000_000]` is reserved for built-ins/extensions — application code stays outside it. Advisor anchors: `Orders.Base=100M`, `Extension=400M`, `Max=900M`; built-ins step by `+10M`.
- Entity/trait string maps use nullable values (`Dictionary<string, string?>`). The gRPC transport writes a null map value as a key-only entry; proto3 readers see an empty string.
- Flow execution observes exactly one scoped `IServiceProvider` per run: `FlowResolver`/`FlowConditionContext` require it; DSL contexts and advisors share it.
- Scheduling execution gating ships as `IJobExecutionAdvisor` (advisor idiom: `Continue` fires, `Block` records `Blocked`, `Handle` records `Skipped`). `IJobLifecycleObserver` is notification-only (`OnScheduled`/`OnUnscheduled`/`OnTriggered`/`OnSucceeded`/`OnFailed`/`OnBlocked`/`OnSkipped`; the last two carry default no-op bodies, so existing implementations compile untouched).
- Conventional Commits enforced in CI (cocogitto); changelog generated to `docs/CHANGELOG.md` via `cog.toml`.
- Tests: xUnit + Moq. Classes `<Subject>Should`, methods `Pascal_Snake_Case` behavior names. Integration projects: `*.Integration.Tests` + `Fixtures/WebAppFactory.cs` + `GenerateProgramFile=false`.

## ANTI-PATTERNS (THIS PROJECT)

- Never edit `eng/common/**` (Arcade-generated) or `tests/Schemata.Flow.Bpmn.Conformance.Tests/PendingCatalog.cs` (conformance exclusion source of truth).
- Never register advisors with `AddScoped(typeof(...))` — use `TryAddEnumerable`; `AddScoped` silently replaces the chain.
- `AdviceContext` is not thread-safe; never share one across concurrent pipelines.
- `CommitChanges` advisors do NOT run on rollback — cache eviction can leave stale entries until TTL.
- Flow engines (`IFlowRuntime`) never load or persist state — handlers persist the returned snapshot.
- Referential integrity for `[ResourceReference]` is enforced at write time by `AdviceValidateResourceReferences`, deliberately not by ORM FKs — do not add ORM associations for it.
- Event wire names must be registered via `RegisterEvent<T>(name)`; CLR type names are never used on the wire.
- Publish domain events from committed advisors, never from mutation advisors (the outbox row is recorded pre-commit).
- Source comments carry no TODO/FIXME/HACK markers by convention; gotchas live in XML doc remarks and docs "Common pitfalls" sections.

## COMMANDS

```bash
./eng/common/build.sh --restore --build --test        # canonical local build + unit tests
./eng/common/build.sh --restore --build --test --integrationTest \
  /p:RestoreDotNetWorkloads=true -c Release           # CI parity
dotnet build src/Schemata.Core/Schemata.Core.csproj -c Release   # single project
dotnet test tests/Schemata.Flow.Bpmn.Conformance.Tests/Schemata.Flow.Bpmn.Conformance.Tests.csproj \
  -c Release --no-restore --filter "Pending!=true"    # BPMN MIWG conformance suite
```

Requires .NET SDK 10.0.201 (`global.json`; `eng/common/tools.sh` bootstraps it). Clone with `--recurse-submodules` — CEL and BPMN conformance tests read from `specs/`.

## NOTES

- `Schemata.Flow.Bpmn` is intentionally bundled in no meta-target; consumers add an explicit `PackageReference`.
- `.skm` model files activate via `<AdditionalFiles Include="*.skm" />`; `Object` views, `Index` pointers, and field options are parsed but emit no C# yet.
- Targets meta-packages pack analyzer DLLs from `artifacts/bin/...` — generators must be built first.
- `docs/_site/` and `docs/api/` are committed DocFX outputs; never hand-edit.
