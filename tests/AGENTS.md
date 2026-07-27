# tests — Test Projects

37 projects in two layers. No e2e layer.

| Layer | Suffix | Count | Notes |
|---|---|---|---|
| unit | `*.Tests` | 26 | in-process xUnit, no external services |
| integration | `*.Integration.Tests` | 11 | local SQLite only; 6 of the 11 also use `WebApplicationFactory<Program>` |

The 11 integration projects: Authorization, Entity.EntityFrameworkCore, Entity.LinqToDB, Flow, Insight.Grpc, Insight.Http, Mapping.AutoMapper, Mapping.Mapster, Report, Resource.Grpc, Resource.Http.

**No external service is needed to run the suite.** Backends are in-memory SQLite (`Data Source=:memory:`) everywhere except `Schemata.Report.Integration.Tests`, which uses a temp-file SQLite database. There is no Docker, no Testcontainers, no RabbitMQ broker and no `UseInMemoryDatabase` anywhere under `tests/`.

## Framework & Runner

- **xUnit** (asserted by source patterns: `[Fact]`, `[Theory]`, `IClassFixture`, `ICollectionFixture`, `IAsyncLifetime`, `[CollectionDefinition]`). The package reference is injected by the Arcade test SDK — there is no explicit `<PackageReference Include="xunit*">` in any test csproj.
- **VSTest runner** via `UseVSTestRunner=true` in [../Directory.Build.props](../Directory.Build.props#L96-L102).
- **Coverage** via `coverlet.collector` (added globally to every test project). Args set in [../Directory.Build.targets](../Directory.Build.targets#L13-L16): `--collect "XPlat Code Coverage;Format=opencover;Include=[Schemata.*]*;Exclude=[*.Tests]*"`.
- **Run**: from repo root, `.\eng\common\Build.ps1 -test` (unit) or `.\eng\common\Build.ps1 -test -integrationTest` (both).

## Naming

- Test class name **ends in `Should`**. File name matches: `ResourceNameDescriptorShould.cs`.
- Test method names describe behaviour: `Action_Condition_Expected`. Examples: `ParseCanonicalName_ExtractLeafAndParentValues`, `RejectsDeviceNameContainingDot`.
- Integration tests tag with `[Trait("Category", "Integration")]`. gRPC integration tests also use `[Collection("GrpcIntegration")]`.

## Layout Inside a Test Project

- `Fixtures/` — `IClassFixture` / `ICollectionFixture` types plus seed-data helpers.
- `Program.cs` — required only for the six projects that use `WebApplicationFactory<Program>` (Authorization, Insight.Http, Insight.Grpc, Report, Resource.Http, Resource.Grpc); it is the test host startup. Four of them locate the project root through `Microsoft.AspNetCore.Testing.ApplicationRootPath`, declared as an `<AssemblyMetadata>` item in the csproj: Resource.Http, Insight.Http, Insight.Grpc, Report. Authorization and Resource.Grpc do **not** declare it — Authorization's `WebAppFactory` walks up the directory tree to find the project root instead.
- `*Should.cs` files in feature-named folders (e.g. `Common/`, `Conformance/`, `Resource/`).

There is **no shared test utility project**. Helpers are duplicated across projects as a deliberate choice — keeps each test project self-contained.

## Conventions Worth Knowing

- **EF / LinqToDB integration tests** use `Fixtures/IntegrationFixture.cs` implementing `IAsyncLifetime`. They create/teardown a SQLite or in-memory DB per fixture.
- **LinqToDB fixtures use a fixture-private `MappingSchema`.** Mutating `MappingSchema.Default` from a fixture races parallel test classes and corrupts linq2db's entity-descriptor caches (symptom: intermittent `no such table`).
- **`GrpcTestCollection.cs` exists in `Schemata.Resource.Grpc.Integration.Tests` only** — it wraps `WebApplicationFactory` in a shared collection so the server starts once. `Schemata.Insight.Grpc.Integration.Tests` does not use it; it takes `IClassFixture<WebAppFactory>` instead.
- **Web integration tests** use `Fixtures/WebAppFactory.cs` which derives from `WebApplicationFactory<Program>` and overrides `ConfigureWebHost` to swap auth, DB, and clock services.
- **CEL conformance tests** ([Schemata.Expressions.Cel.Tests/Conformance/](Schemata.Expressions.Cel.Tests/Conformance/)) read raw `.textproto` vectors from the `specs/cel` submodule via a hard-coded `../../../../../specs/cel/tests/simple/testdata/{suite}.textproto` path. A local [cel-spec-skips.txt](Schemata.Expressions.Cel.Tests/Conformance/cel-spec-skips.txt) filters out-of-scope cases.
- **`Schemata.Modeling.Generator.Tests`** drives the `.skm` source generator; the only checked-in vector is [Schemata.Modeling.Generator.Tests/vector1.skm](Schemata.Modeling.Generator.Tests/vector1.skm).

## Anti-Patterns

- **Do NOT** add a `Microsoft.NET.Test.Sdk` package reference — the Arcade test SDK adds it for you (driven by `IsTestProject=true`).
- **Do NOT** add `coverlet.collector` directly — it is injected by [../Directory.Build.props](../Directory.Build.props#L103-L109).
- **Do NOT** rename a test class away from the `Should` suffix; tooling and existing patterns rely on it.
- **Do NOT** introduce a top-level shared `Schemata.Testing` helper project without RFCing — the per-project duplication is intentional.
- **Do NOT** push `[Trait("Category","Integration")]` onto unit-style projects to opt them into the integration runner; convert the whole project (rename to `*.Integration.Tests`) instead.

## Notes

- Resolved versions: xUnit 2.9.3, `xunit.runner.visualstudio` 3.1.3, `Microsoft.NET.Test.Sdk` 18.0.1 — none appear as explicit `PackageReference`s; the Arcade test SDK injects them. Moq 4.20.72 is referenced explicitly where used. Assertions are plain `Xunit.Assert`; there is no FluentAssertions or Shouldly anywhere in the tree.
- `Schemata.Modeling.Generator.Tests` targets **`net10.0` only**, unlike every other test project, which multi-targets `net8.0;net10.0`.
- `Schemata.Flow.Bpmn.Conformance.Tests` sets `IsUnitTestProject=false` in its csproj, so despite the `*.Tests` suffix it is not treated as a plain unit-test project.
- A BPMN vector that is absent from `PendingCatalog.cs` but still fails to parse, validate or execute makes the suite **fail** through a `FailUncatalogued` path — it is never silently skipped. That is what keeps `PendingCatalog.cs` honest as the exclusion source of truth.
- No `xunit.runner.json`, `nunit*.config`, `mstest*.config`, or `.runsettings` are checked in — runner config is whatever VSTest defaults to plus the global args in `Directory.Build.targets`.
