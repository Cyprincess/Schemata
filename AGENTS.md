# Repository Guidelines

## Project Overview

Schemata is a modular .NET application framework for building extensible business applications with AIP-aligned resource primitives and documented compliance gaps. It ships as 77 `Schemata.*` NuGet packages built from `src/`, targeting `net8.0;net10.0` (source generators target `netstandard2.0`). Consumers bootstrap with `WebApplicationBuilder.UseSchemata(...)`; capabilities compose through a three-phase feature lifecycle. The public API is extension methods in the `Microsoft.AspNetCore.Builder` / `Microsoft.Extensions.DependencyInjection` namespaces; there is no `Program.cs` in this repo.

## Architecture & Data Flow

**Feature lifecycle** (`src/Schemata.Core/Features/ISimpleFeature.cs`) runs in three phases:
- `ConfigureServices` — register services/options; sequenced by `Order` (ascending).
- `ConfigureApplication` — insert middleware; sequenced by `Priority` (ascending).
- `ConfigureEndpoints` — map routes; sequenced by `Priority`. Runs only when an `EndpointDataSource` is registered.

`IFeature` (`src/Schemata.Abstractions/IFeature.cs`) carries `Order` + `Priority`; `FeatureBase` defaults `Order => Priority`. Ordering anchors live in `SchemataConstants.Orders` (`Base = 100_000_000`, `Extension = 400_000_000`, `Max = 900_000_000`); the reserved range is a convention, not code-enforced.

**Bootstrap:** `UseSchemata` (`src/Schemata.Core/Extensions/WebApplicationBuilderExtensions.cs`) → `AddSchemata` (`ServiceCollectionExtensions.cs`) builds `SchemataBuilder` and registers `SchemataStartup` as the single `IStartupFilter`, which drives `UseSchemata` middleware → `UseEndpoints` → `CleanSchemata`.

**Request dispatch:** `InProcessRequestDispatcher` (`src/Schemata.Messaging.Skeleton/Internal/InProcessRequestDispatcher.cs`) answers `IRequestDispatcher`/`ICommandDispatcher`/`IQueryDispatcher` from one implementation. For a command/query it composes registered `IRequestPipelineAdvisor<TRequest,TResponse>` wraps in ascending `Order` around one lazily resolved handler. Before segments run in ascending order; after segments unwind in reverse. A plain `IRequest<T>` runs no wrap chain.

**Security boundary:** `SecurityOrders` fixes dispatcher-wrap order: Authentication, Authorization, Sanitize, Validation, Idempotency, ResponseFamily. `WithAuthentication<TBuilder>(string?)` and `WithAuthorization<TBuilder>()` are the two shared `IResourceBuilder` extensions in Security.Foundation. Each domain supplies a `ResourceSecurityRegistration`; registration determines whether that domain's closed advisor types run. Authentication and coarse permission matching run in wraps; instance access and entitlement filtering stay in domain handler stages. `IAccessProvider` customizes instance policy, while `IPermissionResolver` and `IPermissionMatcher` customize coarse policy.

**Advisor pipeline (handler stages):** `Advisor.For<TAdvisor>()` (`src/Schemata.Advice/Advisor.cs`) returns a token; source-generated `RunAsync` extensions (`generators/Schemata.Advice.Generator/`) dispatch to `AdviceRunner<...>`, which resolves `IAdvisor<...>` from the ambient `AdviceContext`, sorts by `Order`, and short-circuits on the first non-`Continue`. `AdviceContext` (`src/Schemata.Abstractions/Advisors/AdviceContext.cs`) carries pipeline coordination and configuration markers. Business payloads remain in request/response envelopes or local variables.

**Package taxonomy:** `Schemata.<Domain>.<Layer>` — `Skeleton` (contracts), `Foundation` (implementation + `Use{Domain}` registration), `Http`/`Grpc` (transport), bridges (`Event`/`Actor`/`Scheduling`), and provider adapters (`Entity.EntityFrameworkCore`, `Entity.LinqToDB`, `Caching.Redis`, `Mapping.AutoMapper`, `Mapping.Mapster`, `Validation.FluentValidation`, `Messaging.RabbitMq`, `Expressions.Cel`, `Expressions.Aip`). Kernel packages (`Core`, `Common`, `Abstractions`, `Advice`, `Modular`) and `Resource.*`/`Entity.*`/`Transport.*` carry no `Skeleton`.

## Key Directories

| Path | Purpose |
|---|---|
| `src/` | 77 packages, `<Domain>.<Layer>` naming |
| `src/Schemata.Core/` | Bootstrap, feature engine, `SchemataBuilder`, startup filter, built-in features, resource building kernel (`Building/`: `SchemataResourceBuilder`, `ResourceRegistry`, wiring) |
| `src/Schemata.Abstractions/` | Contracts: `IAdvisor`, `AdviceContext`, `SchemataConstants`, exceptions, entity traits |
| `src/Schemata.Advice/` | `Advisor.For`, `AdvicePipeline<T>`, `AdviceRunner` overloads |
| `src/Schemata.Messaging.Skeleton/` | `IRequest`/`ICommand`/`IQuery`, `IRequestHandler`, `IRequestPipelineAdvisor`, dispatcher |
| `src/Schemata.Common/` | `ResourceNameDescriptor`, wire-name rules, error factories |
| `src/Schemata.Resource.Foundation/` | AIP CRUD pipeline: handlers, advisors, default handlers, pipeline wiring |
| `src/Schemata.Entity.Repository/` | Repository base + persistence advisors (soft-delete, canonical name, timestamps) |
| `generators/` | `Schemata.Advice.Generator` (advisor `RunAsync` codegen), `Schemata.Modeling.Generator` (`.skm` DSL) |
| `targets/` | MSBuild meta-packages (`Application`/`Business`/`Module` families) selecting features via `Use*` flags |
| `tests/` | 52 xUnit projects (`*.Tests`, `*.Integration.Tests`, `*.Conformance.Tests`) |
| `docs/` | DocFX site: `guides/`, `cookbook/`, `documents/`, `modeling/` |
| `eng/` | Arcade build scripts and signing assets (`eng/common/` is upstream-generated) |
| `specs/` | Git submodules: `specs/cel`, `specs/bpmn` (required by tests) |

### AIP design documentation

| Task | Canonical document |
|---|---|
| First resource-oriented API design | `docs/guides/aip-resource-design.md` |
| Entity/resource/field modeling | `docs/documents/resource/aip-modeling.md` |
| Standard/custom methods and final wire contracts | `docs/documents/resource/aip-interactions.md` |
| Business logic, Flow, Scheduling, errors, authorization | `docs/documents/resource/aip-business-logic.md` |
| Implementing a resource state transition | `docs/cookbook/resource-business-action.md` |

## Development Commands

Requires .NET SDK **10.0.201** (`global.json`, `rollForward: major`; Arcade SDK `10.0.0-beta.26080.4`). Initialize submodules first:

```bash
git submodule update --init --recursive              # REQUIRED: CEL + BPMN test vectors

# Full local loop (Linux/macOS, Arcade)
./eng/common/build.sh --restore --build --test --integrationTest --configuration Release

# Windows equivalent
.\eng\common\Build.ps1 -restore -build -test -integrationTest -configuration Release

# CI parity (Linux/macOS): adds --pack --publish --ci
./eng/common/cibuild.sh -configuration Release -prepareMachine -integrationTest /p:RestoreDotNetWorkloads=true

# Single project
dotnet build src/Schemata.Core/Schemata.Core.csproj -c Release
dotnet test  tests/Schemata.Core.Tests/Schemata.Core.Tests.csproj -c Release

# Filtered test
dotnet test tests/Schemata.Messaging.Skeleton.Tests -c Release --filter "FullyQualifiedName~RequestDispatcherShould"

# Docs (DocFX)
docfx metadata docs/docfx.json && docfx build docs/docfx.json
```

Flag meaning: `--restore` fetch packages, `--build` compile, `--test` unit tests, `--integrationTest` integration tests, `--pack`/`--publish` NuGet output, `--ci` CI mode. The whole solution is `Schemata.slnx` (XML format); `dotnet build Schemata.slnx` builds everything.

## Code Conventions & Common Patterns

Deviations from stock .NET (from `Directory.Build.props`):
- `LangVersion=preview`, `Nullable=enable`, `ImplicitUsings=false` — **explicit usings everywhere**. No `.editorconfig`; style is enforced only by .NET analyzers (`AnalysisLevel=preview`).
- `ConfigureAwait.Fody` weaves `ConfigureAwait(false)` into every `await` in `src/*` — never write it by hand. Each package has a `FodyWeavers.xml`.
- Central Package Management (`Directory.Packages.props`, net8/net10 conditional blocks) — never put `Version=` on a `PackageReference`. Repo version is `10.0.0-preview` (`eng/Versions.props`).
- Assemblies are strong-named from `eng/key.snk` (`PublicKeyToken=3d9e3b8396b66b15`).
- Every `src/*` project auto-references the `Schemata.Advice.Generator` analyzer unless `SchemataSkipGenerators=true`.

Patterns to follow:
- **Advisor registration uses `TryAddEnumerable`, never plain `AddScoped`** — the pipeline resolves a collection, and enumerable registration preserves the chain while preventing duplicate descriptors. Applies to open-generic advisors too.
- `Order` sequences `ConfigureServices`; `Priority` sequences the middleware/endpoint pipeline.
- Dispatch is registered with `TryAddScoped<InProcessRequestDispatcher>` plus forwarded interface slots per domain capability.
- Handlers register behind `IRequestHandler<,>` (often keyed) so a wrapper can override without replacing the built-in (`AddHandler` in `src/Schemata.Resource.Foundation/Extensions/ServiceCollectionExtensions.cs`).
- Errors follow AIP-193: exceptions derive from `SchemataException` (`src/Schemata.Abstractions/Exceptions/`); factories in `src/Schemata.Common/Errors/SchemataResourceErrors.cs`; reason constants in `SchemataConstants.ErrorReasons`.
- Canonical names use `[CanonicalName("collection/{placeholder}")]` + `ResourceNameDescriptor` (`src/Schemata.Common/`). Addressable patterns end in `{placeholder}` after a collection literal.
- JSON wire format is snake_case with kebab-case enums (`AddSchemataJsonSerializer`).
- AIP claims are requirement-level: read the exact `https://google.aip.dev/<number>` page and classify each rule as `Enforced`, `Supported by extension point`, `Application responsibility`, `Partial`, `Not implemented`, or `Not applicable`.
- Judge resource contracts after the full path: wire binding, `SchemataJsonTraits` / `SchemataProtoModelConfigurator`, advisors, mapping, repository work, result envelope, and final HTTP JSON or gRPC protobuf. A CLR mismatch alone is not a gap; a matching field name alone is not compliance.
- Keep entity, request, detail, and summary roles explicit. Use standard methods before custom methods; use a custom method for a named state transition or side effect instead of disguising it as Update.
- Put persistence invariants in repository advisors, request policy in resource/request advisors, one action in one handler, durable multi-step processes in Flow, and deferred/periodic background execution in Scheduling.
- Resource authorization advisors are activated explicitly through `WithAuthorization()`; `WithAuthentication()` independently activates the authentication wraps. Registering default security services alone does not protect domain operations.

Avoid these AIP errors:
- Never infer requirements from an AIP number, title, code comment, or existing documentation.
- Never advertise an extension point, static detail/summary projection, or Schemata `Operation` type as full AIP support without tracing every required behavior and both transports.
- Never put a long-running or multi-step state machine in an advisor, or duplicate one business action across a handler, Flow, Job, and transport.

## Important Files

- `src/Schemata.Core/Extensions/WebApplicationBuilderExtensions.cs` — `UseSchemata` bootstrap entry.
- `src/Schemata.Core/SchemataStartup.cs` — the only `IStartupFilter`; drives the pipeline.
- `src/Schemata.Core/SchemataBuilder.cs` — `AddFeature<T>` / `Configure` / `Invoke`; sorts features.
- `src/Schemata.Messaging.Skeleton/Internal/InProcessRequestDispatcher.cs` — unified command/query/request dispatch.
- `generators/Schemata.Advice.Generator/AdvicePipelineGenerator.cs` — emits `RunAsync` per advisor interface.
- `Directory.Build.props` — root switchboard, gated on project location (`src`/`tests`/`generators`/`targets`).
- `global.json`, `Directory.Packages.props`, `eng/Versions.props`, `Schemata.slnx` — SDK, package versions, product version, solution.

## Runtime/Tooling Preferences

- **Runtime:** .NET SDK 10.0.201; ASP.NET Core runtime 8.0.25 also required (`global.json` `tools.runtimes`).
- **Build system:** dotnet Arcade SDK via `eng/common/` scripts; do not edit `eng/common/**` (upstream-generated).
- **Package manager:** NuGet with Central Package Management; add package versions in `Directory.Packages.props`.
- **Source generators:** `Schemata.Advice.Generator` is auto-wired to `src/*`; `Schemata.Modeling.Generator` (`.skm` DSL) is bundled through `targets/` meta-packages, not `src/*`.
- **Commits:** Conventional Commits, checked in CI by `cocogitto` (`cog check`, `cog.toml`). Changelog is generated to `docs/CHANGELOG.md` from `docs/CHANGELOG.tera`; do not hand-edit the machine-written section.

## Testing & QA

- **Framework:** xUnit v2 + Moq; assertions are plain `Xunit.Assert` (no FluentAssertions/Shouldly).
- **Naming:** classes `<Subject>Should` (e.g. `PredicateShould`, `RequestDispatcherShould`); methods `Pascal_Snake_Case` (e.g. `Dispatch_ForACommand_RunsThePipelineChainAroundTheHandler`).
- **Layers:** `tests/Schemata.*.Tests` (unit/component) and `tests/Schemata.*.Integration.Tests` (integration; `GenerateProgramFile=false`, own `Program`/`WebApplicationFactory`). Integration tests use **local SQLite only** — no Docker, Testcontainers, or live broker. Coverage runs via coverlet (`Coverage=true` for `tests/*`).
- **Fixtures:** static `*TestHost` helpers for DI setup (`tests/Schemata.Report.Tests/ReportTestHost.cs`); `IAsyncLifetime` fixtures for SQLite integration; `WebApplicationFactory<Program>` via `IClassFixture` for HTTP/gRPC.
- **Conformance suites** read from `specs/` submodules and are gated:
  - BPMN MIWG (`tests/Schemata.Flow.Bpmn.Conformance.Tests`) runs Windows-only in CI: `dotnet test ... --filter "Speed!=Fast"`; exclusions live in `PendingCatalog.cs`, a guard theory re-runs catalogued vectors to catch stale entries, and `Speed=Fast` picks one vector per MIWG case id for local loops.
  - CEL spec (`tests/Schemata.Expressions.Cel.Tests/Conformance/`) skips cases listed in `cel-spec-skips.txt`.
