# Agent Instructions

.NET application framework for modular, extensible business applications. Multi-target `net8.0;net10.0` runtime; `netstandard2.0` generators.

## Layout

```
src/         59 runtime projects, Skeleton/Foundation/Provider triplets
tests/       30 xUnit projects, suffix tells unit vs integration
generators/  2 source generators (netstandard2.0, packed as analyzers)
targets/     3 meta-package families (Application, Business, Module)
eng/         Arcade SDK build infrastructure (eng/common/)
docs/        DocFX site - guides, cookbook, documents, modeling
specs/       external spec sources and test vectors; currently the Google CEL submodule, future spec fixtures land here
```

`Schemata.slnx` is the solution; `Directory.Build.props/targets/Packages.props` drive every csproj.

## Package Layer Convention

| Layer | Suffix | Purpose |
|---|---|---|
| Contracts | `*.Skeleton` | Interfaces, base classes, abstract types |
| Implementation | `*.Foundation` | Concrete logic + feature registration (`ISimpleFeature`) |
| Provider | `*.Redis`, `*.RabbitMq`, etc. | Specific technology adapter |

Foundation depends on its own Skeleton. Providers depend on Skeleton only. Bridge packages (`*.Event`, `*.Scheduling`, `*.Http`, `*.Grpc`) wire one foundation to another via a sub-builder.

## Feature Architecture

Features implement `ISimpleFeature` or extend `FeatureBase` ([src/Schemata.Core/Features/](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Core/Features/)). Register via `SchemataBuilder.AddFeature<T>()`; activate via `Use*()` extension on `SchemataBuilder` or a sub-builder.

Three lifecycle phases, all sorted before run:

1. `ConfigureServices(...)` - ordered by `Order` - run from `SchemataBuilder.Invoke()`
2. `ConfigureApplication(...)` - ordered by `Priority` - run from `SchemataStartup`
3. `ConfigureEndpoints(...)` - ordered by `Priority` - run from `SchemataStartup` inside `UseEndpoints`

`FeatureBase` defaults `Order = Priority` and `Priority = int.MaxValue`; concrete features override `Priority` at minimum.

`SchemataConstants.Orders` ([src/Schemata.Abstractions/SchemataConstants.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Abstractions/SchemataConstants.cs)): `Base = 100_000_000`, `Extension = 400_000_000`, `Max = 900_000_000`. Range `[Base, Max]` is reserved. Authoritative slot table lives in [README.md](file:///D:/source/repos/Cyprin/Schemata/README.md) and [docs/documents/core/built-in-features.md](file:///D:/source/repos/Cyprin/Schemata/docs/documents/core/built-in-features.md) - adding a feature between two existing ones requires checking both.

`[DependsOn<T>]` auto-registers the dependency; `[DependsOn("Type, Assembly")]` is logged but does not auto-register.

## Code Conventions

- Nullable enabled globally, **implicit usings disabled**
- LangVersion: preview, AnalysisLevel: preview
- File-scoped namespaces (`namespace Foo.Bar;` - semicolon, no braces)
- Every `src/` project includes a `FodyWeavers.xml` with `ConfigureAwait ContinueOnCapturedContext="false"` and auto-references `Schemata.Advice.Generator` as analyzer (suppress via `SchemataSkipGenerators=true`)
- Strong-named assemblies in `src/` and `targets/`; not in tests
- Suppressed globally: `CS1591;NU5118;NU5128;AD0001`
- `CentralPackageTransitivePinningEnabled=false` - do not add `Version=` to consuming `PackageReference`s
- No `.editorconfig`, `stylecop.json`, or ruleset file - analysis is driven by props/targets

## Testing

- xUnit (VSTest runner) + Moq, coverlet `opencover` format
- **Test classes**: `XxxShould` suffix (`ConfiguratorsShould.cs`, `TokenHandlerShould.cs`)
- **Methods**: `PascalCase_Underscore` (`Set_Get_RoundTrip`, `IsMatch_ExactMatch_ReturnsTrue`)
- `tests/Schemata.*.Tests/` = unit; `tests/Schemata.*.Integration.Tests/` = integration
- No shared abstract base; fixtures are concrete and project-local (`WebAppFactory`, `IntegrationFixture`, `ProcessRuntimeFixture`, `HandlerFixture`, `GrpcTestCollection`)
- Integration tests carry `[Trait("Category","Integration")]`

## Dependencies & Tooling

- SDK pinned to `10.0.201` in [global.json](file:///D:/source/repos/Cyprin/Schemata/global.json), `allowPrerelease: true`, `rollForward: major`; Arcade SDKs at `10.0.0-beta.26080.4`
- Repo version `10.0.0-preview` in [eng/Versions.props](file:///D:/source/repos/Cyprin/Schemata/eng/Versions.props); bump here for releases
- Central Package Management: [Directory.Packages.props](file:///D:/source/repos/Cyprin/Schemata/Directory.Packages.props) - TF-conditional package versions for `net8.0` vs `net10.0`
- Conventional commits enforced by cocogitto ([cog.toml](file:///D:/source/repos/Cyprin/Schemata/cog.toml)); `chore` omitted from changelog; skip token `[skip ci]`
- Changelog: `docs/CHANGELOG.md` via `docs/CHANGELOG.tera`
- Documentation: DocFX ([docs/docfx.json](file:///D:/source/repos/Cyprin/Schemata/docs/docfx.json)) - metadata pass uses `SchemataSkipGenerators=true` and `TargetFramework=net10.0`
- `specs/` holds external specification sources and conformance test vectors consumed by tests. Today it carries the `specs/cel` submodule (Google CEL); additional spec fixtures (own files or further submodules) belong under the same root. Always clone `--recurse-submodules`

## Package Feeds

[NuGet.config](file:///D:/source/repos/Cyprin/Schemata/NuGet.config) clears defaults; only `nuget`, `dotnet-eng`, and `dotnet-tools` are allowed (the last two are needed for Arcade SDK packages).

## CI

- [.github/workflows/build.yml](file:///D:/source/repos/Cyprin/Schemata/.github/workflows/build.yml) - matrix (mac/linux/win) via Arcade `cibuild.sh` / `CIBuild.cmd`; pushes NuGet packages from `master`/tags
- [.github/workflows/analysis.yml](file:///D:/source/repos/Cyprin/Schemata/.github/workflows/analysis.yml) - SonarCloud + cocogitto check
- [.github/workflows/docs.yml](file:///D:/source/repos/Cyprin/Schemata/.github/workflows/docs.yml) - DocFX → GitHub Pages

All workflows require `submodules: recursive` and cache `.dotnet` + `.packages`.

## Rules Not To Break

- Do not violate the reserved `[100_000_000, 900_000_000]` slot range when registering features
- Generators target `netstandard2.0` only; runtime packages target `net8.0;net10.0`
- Resource handlers must implement `IResourceMethodHandler<TEntity,TRequest,TResponse>` or registration fails
- Tenant overrides must be `Singleton`; `Scoped`/`Transient` are rejected at build time
- `UseModularTargets=true` is required for module stamping to emit `[Module]` attributes
- Built-in soft-delete advisors apply only when the entity implements `ISoftDelete`
- `Schemata.Application.Complex.Targets` does not include `Flow.Foundation` or `Scheduling.Foundation` - reference them explicitly when needed
- `#if NET10_0_OR_GREATER` branches exist in [SchemataBuilderExtensions.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Core/Extensions/SchemataBuilderExtensions.cs) (forwarded headers) and [Identifiers.cs](file:///D:/source/repos/Cyprin/Schemata/src/Schemata.Common/Identifiers.cs) (GUID v7); never collapse them
