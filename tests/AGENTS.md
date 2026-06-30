# tests — Test Projects

30 projects in two layers. No e2e layer.

| Layer | Suffix | Count | Notes |
|---|---|---|---|
| unit | `*.Tests` | 20 | in-process xUnit, no external services |
| integration | `*.Integration.Tests` | 10 | use `WebApplicationFactory<Program>` and/or real-ish backends (SQLite, EF InMemory, RabbitMQ) |

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
- `Program.cs` + `Properties/AssemblyInfo.cs` — required only for integration tests that use `WebApplicationFactory<Program>`. The `Program.cs` is the test host startup; `AssemblyInfo.cs` opens visibility to `WebApplicationFactory`.
- `*Should.cs` files in feature-named folders (e.g. `Common/`, `Conformance/`, `Resource/`).

There is **no shared test utility project**. Helpers are duplicated across projects as a deliberate choice — keeps each test project self-contained.

## Conventions Worth Knowing

- **EF / LinqToDB integration tests** use `Fixtures/IntegrationFixture.cs` implementing `IAsyncLifetime`. They create/teardown a SQLite or in-memory DB per fixture.
- **LinqToDB fixtures use a fixture-private `MappingSchema`.** Mutating `MappingSchema.Default` from a fixture races parallel test classes and corrupts linq2db's entity-descriptor caches (symptom: intermittent `no such table`).
- **gRPC integration tests** wrap `WebApplicationFactory` in a shared `GrpcTestCollection.cs` so the server starts once per collection.
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

- `Schemata.Modeling.Generator.Tests` targets the generator (which itself is `netstandard2.0`), so it runs against the multi-TFM matrix like other test projects.
- No `xunit.runner.json`, `nunit*.config`, `mstest*.config`, or `.runsettings` are checked in — runner config is whatever VSTest defaults to plus the global args in `Directory.Build.targets`.
