# SCHEMATA KNOWLEDGE BASE

**Generated:** 2026-07-27 · **Commit:** cb0feca4 (dirty) · **Branch:** master

## OVERVIEW

Modular .NET application framework shipped as 65 NuGet packages (C#, `net8.0;net10.0`, ASP.NET Core). Consumers bootstrap via `WebApplicationBuilder.UseSchemata(...)`; features compose through a three-phase lifecycle (`ConfigureServices` / `ConfigureApplication` / `ConfigureEndpoints` on `ISimpleFeature`). Built with the dotnet Arcade SDK. There is no `Program.cs` in this repo — the entire public API is extension methods living in `Microsoft.AspNetCore.Builder` / `Microsoft.Extensions.DependencyInjection` namespaces.

## STRUCTURE

```
src/         65 packages: <Domain>.<Layer> naming (taxonomy below)
generators/  2 Roslyn source generators (netstandard2.0): Advice codegen + SKM DSL
targets/     10 MSBuild meta-packages in 3 dirs (Application/Business/Module × Bare/Persisting/Complex[/Modular])
tests/       37 xUnit projects (26 *.Tests + 11 *.Integration.Tests); classes named *Should, never *Tests
eng/         Arcade build infra — eng/common/ is generated upstream, DO NOT EDIT
docs/        DocFX site (guides/, cookbook/, documents/, modeling/) + cog CHANGELOG. No rfcs/ dir exists.
specs/       git submodules: specs/cel (google/cel-spec), specs/bpmn (bpmn-miwg-test-suite) — required by tests
artifacts/   Arcade build drop; targets/ packages pack analyzer DLLs from here post-build
```

### Layer taxonomy (`<Domain>.<Layer>`)

| Suffix | Role |
|---|---|
| bare names (`Abstractions`, `Common`, `Core`, `Advice`, `Modular`) | framework kernel |
| `.Skeleton` | contracts: entities, options, advisor interfaces — no DI wiring. Exception: `Identity.Skeleton` also ships concrete stores and a manager, because it *is* the ASP.NET Identity bridge. |
| `.Foundation` | concrete implementation + `UseXxx()` feature registration |
| `.Http` / `.Grpc` | transport adapters; `Transport.Http` / `Transport.Grpc` are the shared plumbing they build on (no Skeleton) |
| `.<Provider>` | vendor adapters: EntityFrameworkCore, LinqToDB, Cache, Owner, Redis, Distributed, RabbitMq, AutoMapper, Mapster, FluentValidation, Identity |
| `.Aip` / `.Cel` / `.Order` | expression-language implementations behind `Expressions.Skeleton` |
| bridges | two-Foundation bridges: Flow.Event, Flow.Scheduling, Scheduling.Event, Push.Scheduling, Report.Scheduling |
| engines | Flow.StateMachine (default, BPMN subset) vs Flow.Bpmn (full BPMN 2.0.2; in NO meta-target) |

17 domains: Authorization, Caching, Entity, Event, Expressions, Flow, Identity, Insight, Mapping, Push, Report, Resource, Scheduling, Security, Tenancy, Transport, Validation.

## WHERE TO LOOK

Package-level `AGENTS.md` files exist where marked; `src/AGENTS.md`, `generators/AGENTS.md`, `targets/AGENTS.md` and `tests/AGENTS.md` cover their directories.

| Task | Location | Canonical doc |
|---|---|---|
| Host bootstrap, feature lifecycle, built-in `UseXxx` | `src/Schemata.Core` (own AGENTS.md) | `docs/documents/core/` |
| Cross-package contracts, `IAdvisor`, traits, `SchemataConstants` | `src/Schemata.Abstractions` (own AGENTS.md) | `docs/documents/entity/traits.md` |
| Advisor pipeline runtime + codegen | `src/Schemata.Advice` (own AGENTS.md) + `generators/` | `docs/documents/advice/` |
| Canonical names, wire-name rules, error factories, hashing | `src/Schemata.Common` (own AGENTS.md) | `docs/documents/resource/resource-naming.md` |
| Repository/UoW + mutation advisors | `src/Schemata.Entity.Repository` (own AGENTS.md) | `docs/documents/repository/` |
| AIP-compliant CRUD resources | `src/Schemata.Resource.Foundation` (own AGENTS.md) + `.Http` / `.Grpc` | `docs/documents/resource/` |
| OAuth 2.0 / OIDC server | `src/Schemata.Authorization.{Skeleton,Foundation}` (both own AGENTS.md) | `docs/documents/authorization.md` |
| ASP.NET Identity integration | `src/Schemata.Identity.Skeleton` (own AGENTS.md) + `.Foundation` | `docs/documents/identity.md` |
| Workflow AST, engine contracts, process builders | `src/Schemata.Flow.Skeleton` (own AGENTS.md) | `docs/documents/flow/ast.md`, `flow/dsl.md` |
| Flow registry, persistence, resource handlers, bridges | `src/Schemata.Flow.Foundation` (own AGENTS.md) | `docs/documents/flow/runtime.md` |
| BPMN 2.0.2 engine | `src/Schemata.Flow.Bpmn` (own AGENTS.md) | `docs/documents/flow/bpmn-engine.md` |
| Cron / periodic / one-time jobs + AIP-151 operations | `src/Schemata.Scheduling.Foundation` (own AGENTS.md) | `docs/documents/scheduling/` |
| Federated read queries, plan pushdown, source drivers | `src/Schemata.Insight.Foundation` (own AGENTS.md) | `docs/documents/insight/` |
| Report definitions, snapshots, chunks, generation | `src/Schemata.Report.Foundation` (own AGENTS.md) | `docs/documents/report.md` |
| Filter/order languages (AIP-160, CEL, AIP-132) | `src/Schemata.Expressions.Skeleton` (own AGENTS.md) + `.Aip` / `.Cel` / `.Order` | `docs/documents/expressions/` |
| RBAC/ABAC claims gate | `src/Schemata.Security.{Skeleton,Foundation}` | `docs/documents/security.md` |
| Multi-tenant resolution + per-tenant DI | `src/Schemata.Tenancy.{Skeleton,Foundation}` | `docs/documents/tenancy.md` |
| Notification fan-out + subscriptions | `src/Schemata.Push.{Skeleton,Foundation,Scheduling}` | `docs/documents/push/` |
| Event bus, wire-name registry, outbox | `src/Schemata.Event.{Skeleton,Foundation,RabbitMq}` | `docs/documents/event/` |
| Cache abstraction + Redis/IDistributedCache adapters | `src/Schemata.Caching.{Skeleton,Distributed,Redis}` | `docs/documents/caching/` |
| SKM DSL → C# entity codegen | `generators/Schemata.Modeling.Generator` (see `generators/AGENTS.md`) | `docs/modeling/`, `docs/ebnf-modeling.txt` |
| Meta-package opt-in flags (`UseDsl`, `UseTenancy`, …) | `targets/*/Directory.Build.props` (see `targets/AGENTS.md`) | per-family `README.md` |
| Scenario pitfalls | `## Common pitfalls` sections in 18 `docs/cookbook/*.md` files (none exist under `docs/documents/`) | — |

## CODE MAP

Dependency backbone (bottom-up): `Abstractions` (zero Schemata deps) → `Common` → `Advice` → `Core` → `Modular`. `Entity.Repository` (deps: Advice, Common, Validation.Skeleton) underpins the Identity / Authorization / Tenancy / Flow / Scheduling / Push Skeletons and Resource.Foundation. Every `*.Foundation` depends on its own Skeleton + `Schemata.Core`. `Report` sits at the top: nothing in `src/` consumes it.

| Symbol | Kind | Location | Role |
|---|---|---|---|
| `UseSchemata(WebApplicationBuilder, …)` | 3 extension overloads | `Core/Extensions/WebApplicationBuilderExtensions.cs` | the one public bootstrap entry point |
| `AddSchemata(IServiceCollection, …)` | 4 extension overloads | `Core/Extensions/ServiceCollectionExtensions.cs` | registers the startup filter, flushes staged services |
| `SchemataBuilder` | sealed class | `Core/SchemataBuilder.cs` | `AddFeature<T>` / `Configure` / `ConfigureServices` / `Invoke`; sorts features by `Order` |
| `SchemataStartup` | sealed `IStartupFilter` | `Core/SchemataStartup.cs` | the ONLY startup filter that drives the pipeline; wraps `UseSchemata` → `UseEndpoints` → `CleanSchemata` |
| `IFeature` | interface | `Abstractions/IFeature.cs` | `Order` (ConfigureServices) + `Priority` (app/endpoint pipeline) |
| `ISimpleFeature : IFeature` | interface | `Core/Features/ISimpleFeature.cs` | the three lifecycle phases; `FeatureBase` defaults `Order => Priority` |
| `IModule : IFeature` | marker | `Abstractions/Modular/IModule.cs` | modules participate in the same ordering |
| `IAdvisor<T1..T16>` | interfaces | `Abstractions/Advisors/IAdvisor.cs` | `AdviseAsync(AdviceContext, …) → Continue \| Block \| Handle` |
| `Advisor.For<T>()` | static | `Advice/Advisor.cs` | returns the `AdvicePipeline<T>` marker struct |
| `AdvicePipelineExtensions.RunAsync` | generated | emitted by `generators/Schemata.Advice.Generator` | one overload per advisor arity, forwards to `AdviceRunner<…>` |

**Advisor pattern** (framework-wide extension point): resolved via `GetServices<TAdvisor>().OrderBy(a => a.Order)`, short-circuits at the first non-`Continue`.

## CONVENTIONS (deviations from stock .NET only)

- `LangVersion=preview`, `Nullable=enable`, `ImplicitUsings=false` (explicit usings everywhere). No `.editorconfig`.
- `TargetFrameworks` is **not** set globally — every `src/*` csproj declares `net8.0;net10.0` itself; generators declare `netstandard2.0` (single-target, required by the Roslyn loader).
- Project behavior is determined by disk location. The root `Directory.Build.props` is the single switchboard, gated on `$(RepoRelativeProjectDir.Contains('src'|'tests'|'generators'|'targets'))`. There are no per-directory `Directory.Build.props` under `src/`, `tests/` or `generators/`; only the three `targets/*` families have their own.
- ConfigureAwait.Fody weaves `ConfigureAwait(false)` into every await in `src/*` — never write it manually.
- Generated `.resx` accessors are rewritten `internal` → `public` by the `OverrideResourcesVisibility` target in `Directory.Build.targets` (three literal `.Replace()` calls after `_GenerateResxSource`).
- Central package versions: `Directory.Packages.props` splits net8.0/net10.0 blocks by `$([MSBuild]::VersionEquals($(TargetFrameworkVersion), '8.0'|'10.0'))`. Repo version lives in `eng/Versions.props` (`10.0.0-preview` until `-p:StabilizePackageVersion=true`).
- Assemblies are strong-named from `eng/key.snk` (`StrongNameKeyId=cyprin`, `PublicKeyToken=3d9e3b8396b66b15`), never delay-signed.
- `Order` sequences `ConfigureServices`; `Priority` sequences the app/endpoint pipeline. Range `[100_000_000, 900_000_000]` is reserved for built-ins/extensions — application code stays outside it. Anchors in `SchemataConstants.Orders`: `Base = 100_000_000`, `Extension = Base + 300_000_000`, `Max = 900_000_000`. The `+10M` cadence between built-in features is a convention held in the table in `src/Schemata.Core/AGENTS.md`, not a code constant.
- Feature registry methods (`AddFeature<T>`, `HasFeature<T>`, `GetFeatures`) are **extension methods** in `Core/Extensions/SchemataOptionsExtensions.cs`, not members of `SchemataOptions`.
- Entity/trait string maps use nullable values (`Dictionary<string, string?>`). The gRPC transport registers them as proto3 maps and writes a null map value as a key-only entry; proto3 readers see an empty string.
- Flow execution observes exactly one scoped `IServiceProvider` per run: the scoped `FlowRunner` hands its provider to `ProcessPersistence`, which joins all five flow repositories into one unit of work; engines are keyed singletons holding no provider.
- Expression languages are keyed DI services (`"aip"` / `"cel"`) — compiler, pushdown planner and descriptor must share the key. `IOrderCompiler` is the one non-keyed exception.
- Scheduling execution gating ships as `IJobExecutionAdvisor` (`Continue` fires, `Block` records `Blocked`, `Handle` records `Skipped`). `IJobLifecycleObserver` is notification-only (`OnScheduled`/`OnUnscheduled`/`OnTriggered`/`OnSucceeded`/`OnFailed`/`OnBlocked`/`OnSkipped`; the last two carry default no-op bodies as optional notification hooks, matching the IEventLifecycleObserver / IProcessLifecycleObserver convention).
- Conventional Commits are checked in CI by the `cocogitto/cocogitto-action` (`cog check`). `cog.toml` configures commit *types* only — there is no `[scopes]` block, so scopes are unenforced. Changelog is generated to `docs/CHANGELOG.md` from the `docs/CHANGELOG.tera` template; everything below the `- - -` separator is machine-written.
- Tests: xUnit + Moq, assertions are plain `Xunit.Assert` (no FluentAssertions). Classes `<Subject>Should`, methods `Pascal_Snake_Case`. Integration projects: `*.Integration.Tests` + `GenerateProgramFile=false`; they use local SQLite only — no Docker, no Testcontainers, no broker.

## ANTI-PATTERNS (THIS PROJECT)

- Never edit `eng/common/**` (Arcade-generated) or `tests/Schemata.Flow.Bpmn.Conformance.Tests/PendingCatalog.cs` (conformance exclusion source of truth).
- Never register advisors with `AddScoped(typeof(...))` — use `TryAddEnumerable`; `AddScoped` silently replaces the chain. There are zero plain `.AddScoped(` calls in `src/` today; non-advisor scoped services use `TryAddScoped`.
- `AdviceContext` is not thread-safe; never share one across concurrent pipelines.
- `IRepositoryCommittedAdvisor` runs only after a successful commit and has no rollback path — cache eviction can leave stale entries until TTL.
- Flow engines (`IFlowRuntime`) never load or persist state — handlers persist the returned snapshot.
- Referential integrity for `[ResourceReference]` is enforced at write time by `AdviceValidateResourceReferences`, deliberately not by ORM FKs — do not add ORM associations for it.
- Event wire names must be registered via `RegisterEvent<T>(name)`; CLR type names are never used on the wire, and an unregistered type throws at publish, not at startup.
- Publish domain events from committed advisors, never from mutation advisors (the outbox row is recorded pre-commit). No advisor in `Entity.Repository` touches `IEventBus` — keep it that way.
- Do not add a `Foundation` reference from another domain's `Skeleton`, and do not introduce a domain prefix without a matching `Foundation` + `Use{Domain}`.
- Source comments carry no TODO/FIXME/HACK markers by convention; gotchas live in XML doc remarks and cookbook "Common pitfalls" sections.

## COMMANDS

```bash
git submodule update --init --recursive               # REQUIRED: CEL + BPMN suites read from specs/

./eng/common/build.sh --restore --build --test        # local dev loop, unit tests only
./eng/common/build.sh --build --test --ci --integrationTest \
  --prepareMachine --warnAsError false                # CI parity (matches .github/workflows/analysis.yml)
./eng/common/cibuild.sh -configuration Release -prepareMachine \
  -integrationTest /p:RestoreDotNetWorkloads=true     # full CI build (adds --pack --publish; build.yml)

dotnet build src/Schemata.Core/Schemata.Core.csproj -c Release     # single project
dotnet test tests/Schemata.Flow.Bpmn.Conformance.Tests/Schemata.Flow.Bpmn.Conformance.Tests.csproj \
  -c Release --no-restore --filter "Pending!=true"    # BPMN MIWG conformance suite

dotnet tool install -g docfx && docfx metadata docs/docfx.json && docfx build docs/docfx.json
```

Requires .NET SDK 10.0.201 (`global.json`, `rollForward: major`; `eng/common/tools.sh` bootstraps it).

## NOTES

- `Schemata.Flow.Bpmn` and every `Schemata.Scheduling.*` package are intentionally bundled in no meta-target; consumers add an explicit `PackageReference`.
- The BPMN conformance job in `.github/workflows/build.yml` is gated to `runner.os == 'Windows'` — macOS and Linux PRs do not exercise it.
- `.config/dotnet-tools.json` is **not** checked in; `analysis.yml` creates it on demand via `dotnet tool install --local --create-manifest-if-needed dotnet-sonarscanner`.
- `docs/docfx.json` passes `SchemataSkipGenerators=true` to MSBuild during API extraction, so generator-emitted members never reach `docs/api/`.
- `.skm` model files activate via `<AdditionalFiles Include="*.skm" />`; `Object` views, `Index` pointers, and field options are parsed but emit no C# yet.
- Targets meta-packages pack analyzer DLLs from `artifacts/bin/...` — generators must be built first or the `<None Include>` resolves to a missing file.
- `docs/_site/` and `docs/api/` are generated DocFX outputs that `.gitignore` excludes from the repo; never hand-edit. `docs/api/` is produced from `src/**` XML doc comments, so a correction to the published API reference is an edit to the source comment.
- `docs/documents/resource/read-pipeline.md` is stale on two points: `IExpressionCompiler` is resolved by the module's resolved language (not fixed to AIP), and `IOrderCompiler` is non-keyed.
