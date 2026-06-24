# tests/

xUnit + Moq + coverlet. Multi-target `net8.0;net10.0`. Unsigned, non-packable.

## Project Suffix Convention

- `tests/Schemata.*.Tests/` - unit tests (no external infra)
- `tests/Schemata.*.Integration.Tests/` - integration tests; carry `[Trait("Category","Integration")]`

20 unit projects + 10 integration projects. Test project root mirrors the corresponding `src/` project (`tests/Schemata.Core.Tests/` ↔ `src/Schemata.Core/`). Generator tests live under `tests/Schemata.Modeling.Generator.Tests/`.

## Naming

- **Files**: `*Should.cs`
- **Classes**: `XxxShould`
- **Methods**: `PascalCase_Underscore` describing behavior, not method called

Examples:

```
ConfiguratorsShould.Set_Get_RoundTrip
DefaultPermissionMatcherShould.IsMatch_ExactMatch_ReturnsTrue
TokenEndpointShould.ClientCredentials_ReturnsAccessToken
CelSpecShould.Pass_InScopeConformanceVectors
SchemataTenantServiceProviderFactoryShould.Applies_Matching_TenantOverrides_To_Built_Container
```

## Fixtures (concrete, project-local; no shared abstract base)

| Fixture | Used by |
|---|---|
| `WebAppFactory` | ASP.NET integration tests (Authorization, Insight, Resource Http) |
| `IntegrationFixture` | EF Core and LinqToDB integration tests |
| `ProcessRuntimeFixture` | Flow tests |
| `HandlerFixture` | Resource handler unit tests |
| `GrpcTestCollection` (`[Collection("GrpcIntegration")]`) | gRPC integration tests - shared because the runtime model has process-wide state |

xUnit patterns in use: `IClassFixture<T>`, `ICollectionFixture<T>`, `IAsyncLifetime`, `[Trait]`, `MemberData(nameof(...))`. **No `InlineData` / `ClassData`** - keep data-driven tests on `MemberData`.

## Coverage

`Directory.Build.targets` enables coverlet with `Format=opencover` and `Include=[Schemata.*]*;Exclude=[*.Tests]*`. Coverage runs unconditionally when `IsTestProject=true`. Reports land under `**/TestResults/**/coverage.opencover.xml` and feed SonarCloud in [analysis.yml](file:///D:/source/repos/Cyprin/Schemata/.github/workflows/analysis.yml).

## Rules

- Do not introduce a shared abstract test base. Convention is concrete fixtures, optionally combined via `IClassFixture` / `ICollectionFixture`.
- Mocking helpers that need `async` for an iterator pattern suppress `CS1998` deliberately - do not "fix" by adding `await Task.Yield()`.
- Integration tests carry `[Trait("Category","Integration")]`. The trait drives selective runs and Sonar filtering; add it to every new integration class.
- Tests are unsigned. Do not add `SignAssembly` to a test csproj.
- Use Moq for mocks; do not introduce a second mocking library.
