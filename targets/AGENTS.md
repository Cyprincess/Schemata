# targets — MSBuild Meta-Packages

10 NuGet packages whose only job is to inject curated sets of references, analyzers, and MSBuild assets into a consumer's csproj. They are how end-users adopt Schemata without naming every individual package.

## Three Families × Three Tiers

```
Application.Targets         Business.Targets         Module.Targets
Application.Persisting.Targets    Business.Persisting.Targets    Module.Persisting.Targets
Application.Modular.Targets       Business.Complex.Targets       Module.Complex.Targets
Application.Complex.Targets
```

| Family | Audience | What "Base" contains |
|---|---|---|
| **Application** | ASP.NET host app | `Schemata.Core` + ASP.NET Core framework reference |
| **Business** | shared domain library (no ASP.NET) | `Schemata.Abstractions` |
| **Module** | plug-in module library loaded by `Schemata.Modular` | `Schemata.Abstractions` |

Each family stacks:

- `Base` — abstractions only.
- `Persisting` — adds `Schemata.Entity.Repository` (`UseRepository=true`).
- `Modular` (Application only) — adds `Schemata.Modular` + auto-emits `[assembly: ModuleAttribute("...")]` from module project/package names.
- `Complex` — Persisting plus DSL + Mapping + Authorization + Identity + Security (+ Validation for Module; + Modular + Tenancy + Resource HTTP/gRPC for Application).

## How Targets Are Wired

Each family lives in `targets/Schemata.{Family}.Targets/`. Inside that directory:

- `Directory.Build.props` — shared MSBuild logic for every variant in the family. Reads `Use{Capability}=true` flags and conditionally adds `<PackageReference>` + analyzer references.
- One `.csproj` per tier — typically a single line of `<PropertyGroup>` setting the relevant `Use*` flags (`UseRepository`, `UseDsl`, `UseAuthorization`, `UseIdentity`, `UseMapping`, `UseSecurity`, `UseValidation`, `UseModular`, `UseTenancy`, `UseResource*`, …).
- `Schemata.{Family}.Targets.props` / `.targets` — packed into the NuGet under `build/` so the consumer's MSBuild auto-imports them.

The `Schemata.Application.Modular.Targets.targets` file is the one piece of real MSBuild logic ([Schemata.Application.Targets/Schemata.Application.Modular.Targets.targets](Schemata.Application.Targets/Schemata.Application.Modular.Targets.targets)): it walks `ModulePackageNames` + module project references and emits `[assembly: ModuleAttribute("…")]` for each.

## Consumer Use

Plain `<PackageReference>` is enough — NuGet pulls the `build/` assets automatically. No `IncludeAssets=build` necessary.

```xml
<!-- App project -->
<PackageReference Include="Schemata.Application.Complex.Targets" />

<!-- Domain library -->
<PackageReference Include="Schemata.Business.Complex.Targets" />

<!-- Module library -->
<PackageReference Include="Schemata.Module.Complex.Targets" />
```

## Conventions

- **One file added to a tier package = touches every consumer**. Treat changes here as breaking.
- **`EnforceExtendedAnalyzerRules=true`** ([../Directory.Build.props](../Directory.Build.props#L67-L69)) — RS-rule violations fail the build.
- **`IncludeBuildOutput=false`** for these projects ([../Directory.Build.props](../Directory.Build.props#L71-L76)) — there is no compiled DLL, only MSBuild assets.
- **Always document the new flag in [Schemata.{Family}.Targets/README.md](Schemata.Business.Targets/README.md)** when you add a `Use*` switch.

## Anti-Patterns

- **Do NOT** add direct `<PackageReference>` to runtime packages from the target csproj. Wire via the family's `Directory.Build.props` conditional on a `Use*` flag.
- **Do NOT** ship a target package that adds a hard dependency on ASP.NET Core from `Business.Targets` — that family is explicitly framework-agnostic.
- **Do NOT** introduce a tier-specific MSBuild item that bypasses the family `Directory.Build.props` — every consumer of every tier must see the same wiring rules.

## Notes

- Per-family README is what nuget.org renders: [Schemata.Application.Targets/](Schemata.Application.Targets/), [Schemata.Business.Targets/README.md](Schemata.Business.Targets/README.md), [Schemata.Module.Targets/README.md](Schemata.Module.Targets/README.md).
- The modeling generator (`.skm` → C#) is packaged into the `Complex` tiers — picking `Complex` is how a consumer "opts in" to SKM.
