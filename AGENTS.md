# Schemata Knowledge Base

**Commit:** `ea5306fd` on `master`

## Overview

Modular .NET application framework. Ships ~60 NuGet packages organized as **feature domains** (Authorization, Caching, Entity, Event, Expressions, Flow, Identity, Insight, Mapping, Push, Resource, Scheduling, Security, Tenancy) plus a composition core (`Schemata.Core`), contracts (`Schemata.Abstractions`), an advisor runtime (`Schemata.Advice`), a module loader (`Schemata.Modular`), and 3 meta-package families (`Schemata.{Application,Business,Module}.Targets`).

## Structure

```
.
├── src/             # 58 runtime packages (net8.0;net10.0)
├── tests/           # 27 test projects (xUnit; unit + integration)
├── generators/      # 2 Roslyn incremental generators (netstandard2.0)
├── targets/         # 10 MSBuild meta-packages (consumer-facing umbrellas)
├── docs/            # DocFX site (guides/cookbook/documents/modeling + api/)
├── eng/             # Arcade SDK shared assets — DO NOT EDIT eng/common
├── specs/cel/       # git submodule → google/cel-spec (conformance vectors)
├── artifacts/       # all bin/obj/log/packages redirected here (Arcade)
├── .github/         # build.yml, analysis.yml (SonarCloud + cocogitto), docs.yml
├── Schemata.slnx    # solution (XML format, not legacy .sln)
├── Directory.Build.props / .targets    # repo-wide MSBuild + Arcade import
├── Directory.Packages.props            # central package versions (CPM)
├── global.json                          # SDK 10.0.201, Arcade SDK pin
└── cog.toml                             # Conventional Commits / changelog config
```

## Where to Look

| Task | Location |
|---|---|
| Add or change a built-in feature | [src/Schemata.Core/Features/](src/Schemata.Core/Features/) |
| Add a new feature domain | new `src/Schemata.{Domain}.{Foundation,Skeleton,...}/` package + see [src/AGENTS.md](src/AGENTS.md) |
| Change OAuth/OIDC server | [src/Schemata.Authorization.Foundation/](src/Schemata.Authorization.Foundation/) |
| Change AIP resource CRUD | [src/Schemata.Resource.Foundation/](src/Schemata.Resource.Foundation/) |
| Change BPMN process engine | [src/Schemata.Flow.StateMachine/](src/Schemata.Flow.StateMachine/), AST in [src/Schemata.Flow.Skeleton/](src/Schemata.Flow.Skeleton/) |
| Edit entity / resource contracts | [src/Schemata.Abstractions/](src/Schemata.Abstractions/) |
| Touch the advice/advisor pipeline | [src/Schemata.Advice/](src/Schemata.Advice/) + generator at [generators/Schemata.Advice.Generator/](generators/Schemata.Advice.Generator/) |
| Touch the `.skm` DSL | [generators/Schemata.Modeling.Generator/](generators/Schemata.Modeling.Generator/) |
| Add a feature to a meta-package | [targets/AGENTS.md](targets/AGENTS.md) |
| Add or rename a package version | [Directory.Packages.props](Directory.Packages.props) (CPM is on, do not put `Version=` on `PackageReference`) |
| Change CI / build matrix | [.github/workflows/build.yml](.github/workflows/build.yml) |
| Change SonarCloud or commit lint | [.github/workflows/analysis.yml](.github/workflows/analysis.yml) |

## Composition Model

A host application calls `WebApplicationBuilder.UseSchemata(...)` ([src/Schemata.Core/Extensions/WebApplicationBuilderExtensions.cs](src/Schemata.Core/Extensions/WebApplicationBuilderExtensions.cs)). That yields a [`SchemataBuilder`](src/Schemata.Core/SchemataBuilder.cs) holding a staging `IServiceCollection`, a `Configurators` registry, and a `SchemataOptions` bag.

Each capability is an **`ISimpleFeature`** ([src/Schemata.Core/Features/ISimpleFeature.cs](src/Schemata.Core/Features/ISimpleFeature.cs)) with three lifecycle hooks: `ConfigureServices`, `ConfigureApplication`, `ConfigureEndpoints`. Features are sorted by two `int` keys:

- `Order` → `ConfigureServices` sequence.
- `Priority` → `ConfigureApplication` + `ConfigureEndpoints` sequence.

The range `[100_000_000, 900_000_000]` is **reserved** for built-in + extension features. See the full priority table in [README.md](README.md). Anchors: `+5M` = sub-feature of a built-in (only `WellKnown` today); `+100K` / `+200K` / `+300K` = bridges stacked above their later-feature anchor (e.g. `Flow.Event`, `Flow.Scheduling`).

**Modules** ([Schemata.Abstractions.Modular.IModule](src/Schemata.Abstractions/Modular/IModule.cs), [ModuleBase](src/Schemata.Abstractions/Modular/ModuleBase.cs)) are plug-in libraries discovered + run by [Schemata.Modular](src/Schemata.Modular/). They share the same `Order`/`Priority` ordering. Methods outside the three lifecycle hooks are never invoked.

## Conventions

- **Target frameworks**: runtime libs target `net8.0;net10.0`; source generators target `netstandard2.0` (Roslyn constraint); test projects mirror runtime libs.
- **Language**: `LangVersion=preview`, `Nullable=enable`, `ImplicitUsings=false`. Write explicit `using` directives.
- **Brace style**: opening `{` on the same line; sealed/abstract used deliberately; expression-bodied members for one-liners.
- **Comments**: triple-slash XML docs for public API on packable assemblies (`GenerateDocumentationFile=true` on `src/*`).
- **Packages**: Central Package Management is on (`ManagePackageVersionsCentrally=true`). Add versions in [Directory.Packages.props](Directory.Packages.props); reference them by `<PackageReference Include="..." />` **without** `Version=`.
- **Signing**: all packable projects strong-name sign with [eng/key.snk](eng/key.snk); `SignAssembly=true` is set by `Directory.Build.props` for `src/`, `generators/`, `targets/`.
- **Artifacts**: Arcade redirects every `bin/`/`obj/`/log/package output to `artifacts/`. Do not commit `artifacts/`.
- **Naming**: domain prefix → role suffix. `Schemata.{Domain}.Skeleton` = contracts; `Schemata.{Domain}.Foundation` = runtime; `.Http` / `.Grpc` = transport adapter; `.Event` / `.Scheduling` = cross-domain bridge; `.EntityFrameworkCore` / `.LinqToDB` / `.Redis` / `.AutoMapper` / `.Mapster` = vendor adapter.
- **Tests**: class name ends in `Should`; method body asserts behaviour. Integration tests carry `[Trait("Category","Integration")]`.

## Anti-Patterns (this project)

- **Do NOT** add `Version=` to `<PackageReference>` — CPM owns versions ([docs/documents/packages.md:200](docs/documents/packages.md)).
- **Do NOT** add a database column for Identity entity `Id` / `ConcurrencyStamp` — they are `[NotMapped]` ([docs/documents/identity.md:176-177](docs/documents/identity.md)).
- **Do NOT** share an `AdviceContext` across concurrent pipelines ([docs/documents/core/advice-pipeline.md:223-225](docs/documents/core/advice-pipeline.md)).
- **Do NOT** edit anything under [eng/common/](eng/common/) — that tree is generated/owned by `dotnet/arcade` automation.
- **Do NOT** invent new `Order`/`Priority` values inside `[100_000_000, 900_000_000]` — that range is reserved.
- **Do NOT** use `as any` equivalents in C#: no `#pragma warning disable` to silence real errors, no empty `catch {}` blocks, no `Suppress` attributes without a tracked reason.
- **Do NOT** assume module hooks other than `ConfigureServices`/`ConfigureApplication`/`ConfigureEndpoints` will run.

## Unique Styles

- **Solution file is `.slnx`**, not `.sln`. Tools that require legacy `.sln` will not work without conversion.
- **Build is Arcade-driven**. The entire build/test/package pipeline goes through `eng/common/Build.ps1` (or `.cmd` / `.sh`), not raw `dotnet build`.
- **Advisor pipeline** is the cross-cutting mechanism for the whole framework. Repository CRUD, HTTP resource handlers, user registration, flow transitions all run through ordered chains of `IAdvisor`. Add cross-cutting behaviour by registering an advisor, not by subclassing.
- **Trait-based entity modelling**. Capabilities are marker interfaces (`ITimestamp`, `ISoftDelete`, `IConcurrency`, `IOwnable`, …) under [src/Schemata.Abstractions/Entities/](src/Schemata.Abstractions/Entities/); built-in advisors react to them with `is`-checks.
- **`.skm` DSL** compiles to C# entity/trait/enum types via [Schemata.Modeling.Generator](generators/Schemata.Modeling.Generator/); the only checked-in vector lives at [tests/Schemata.Modeling.Generator.Tests/vector1.skm](tests/Schemata.Modeling.Generator.Tests/vector1.skm).
- **Two cross-package versioning lines** in [Directory.Packages.props](Directory.Packages.props): a single base set plus per-TFM blocks for Microsoft packages so `net8.0` gets `8.0.x` and `net10.0` gets `10.0.x`.

## Commands

All build/test/pack go through Arcade. From the repo root:

```pwsh
# Local build (Windows)
.\eng\common\Build.ps1 -configuration Release -restore -build

# Local build + tests
.\eng\common\Build.ps1 -configuration Release -restore -build -test

# Local build + unit + integration tests
.\eng\common\Build.ps1 -configuration Release -restore -build -test -integrationTest

# Pack NuGet artifacts → artifacts/packages/Release/Shipping/
.\eng\common\Build.ps1 -configuration Release -restore -build -pack

# CI command used by GitHub Actions (Windows runner)
.\eng\common\CIBuild.cmd -configuration Release -prepareMachine -integrationTest /p:RestoreDotNetWorkloads=true

# Build the DocFX site (requires `dotnet tool install -g docfx`)
docfx metadata docs/docfx.json
docfx build docs/docfx.json   # → docs/_site
```

The .NET SDK is pinned to `10.0.201` ([global.json](global.json)) and bootstrapped into `.dotnet/` by `Build.ps1 -restore` — system SDKs are bypassed.

## Notes

- **`SchemataSkipGenerators=true`** disables the advice generator's auto-attach in [Directory.Build.props](Directory.Build.props). Set this when running `docfx metadata` (already done in [docs/docfx.json](docs/docfx.json)) or any analyzer-incompatible tool.
- **JSON wire format defaults to `snake_case`** with 53-bit-safe integer handling (`SchemataJsonSerializerFeature`). When writing entities that round-trip to JS clients, do not hand-roll a different naming policy.
- **OfficialBuild=true** triggers on GitHub Actions for non-PR pushes — versioning then derives from `_ComputedOfficialBuildId` computed in [.github/workflows/build.yml](.github/workflows/build.yml).
- **Conventional Commits are enforced** via the `cocogitto` job in [analysis.yml](.github/workflows/analysis.yml); see [cog.toml](cog.toml) for type config.
- **CEL conformance corpus** is pulled from the [specs/cel](specs/cel) submodule at fixed commit `cb51b41...`. Update with `git submodule update --remote` only when intentionally rebasing onto a newer cel-spec.

## Sub-directory Knowledge

- [src/AGENTS.md](src/AGENTS.md) — package layout, domain→suffix→dependency map
- [tests/AGENTS.md](tests/AGENTS.md) — test framework, fixtures, naming, layering
- [generators/AGENTS.md](generators/AGENTS.md) — Roslyn incremental generators, hook-up via `OutputItemType=Analyzer`
- [targets/AGENTS.md](targets/AGENTS.md) — consumer meta-packages and MSBuild assets
- [src/Schemata.Core/AGENTS.md](src/Schemata.Core/AGENTS.md) — `SchemataBuilder` + built-in features
- [src/Schemata.Abstractions/AGENTS.md](src/Schemata.Abstractions/AGENTS.md) — entity traits, errors, AIP resource contracts
- [src/Schemata.Advice/AGENTS.md](src/Schemata.Advice/AGENTS.md) — advisor pipeline + generated `RunAsync`
- [src/Schemata.Authorization.Foundation/AGENTS.md](src/Schemata.Authorization.Foundation/AGENTS.md) — OAuth 2.0 / OIDC server
- [src/Schemata.Resource.Foundation/AGENTS.md](src/Schemata.Resource.Foundation/AGENTS.md) — AIP-compliant CRUD core
- [src/Schemata.Flow.Skeleton/AGENTS.md](src/Schemata.Flow.Skeleton/AGENTS.md) — BPMN AST + builder DSL
