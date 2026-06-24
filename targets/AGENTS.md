# targets/

Meta-packages assembled from `src/` projects via opt-in MSBuild toggles. Each family ships three SKUs: base, `Persisting` (adds repository), and `Complex` (adds everything wired in by the family). `Modular` is an extra SKU for the Application family.

```
Schemata.Application.Targets/   ASP.NET web application bootstrap
Schemata.Business.Targets/      Business-layer composition (no host)
Schemata.Module.Targets/        Module project conventions (consumed via UseModularTargets)
```

Each family has its own `Directory.Build.props` that imports the root, multi-targets `net8.0;net10.0`, and conditionally `ProjectReference`s `src/Schemata.*` packages based on `Use*` toggles defined per `*.csproj`.

## Common Toggles

| Toggle | Effect |
|---|---|
| `UseDSLTargets=true` | Packs `Schemata.Modeling.Generator.dll` + `Parlot.dll` as analyzers |
| `UseModularTargets=true` | Adds `Schemata.Modular` ref + includes `*.Modular.Targets.targets` in the package `build/` folder (host module stamping) |
| `UseTenancy=true` | Adds `Schemata.Tenancy.Foundation` |
| `UseAuthorization=true` | Adds `Schemata.Authorization.Foundation` |
| `UseIdentity=true` | Adds `Schemata.Identity.Foundation` |
| `UseMapster=true` | Adds `Schemata.Mapping.Mapster` (not AutoMapper - that's a separate switch) |
| `UseRepository=true` | Adds `Schemata.Entity.Repository` |
| `UseResourceGrpc=true` | Adds `Schemata.Resource.Foundation` + `Schemata.Resource.Grpc` |

The `*.Complex.Targets.csproj` files set most of these; `*.Persisting.Targets.csproj` only sets `UseRepository=true`; base SKUs set none.

## Rules

- `Schemata.Application.Complex.Targets` **does not include** `Schemata.Flow.Foundation` or `Schemata.Scheduling.Foundation`. Reference them explicitly in the consuming project when you need them.
- `Schemata.Application.Modular.Targets.targets` is packed under `build/$(AssemblyName).targets` only when `UseModularTargets=true`; without it, host module stamping does not run and `[Module]` attributes will not be generated.
- Each module assembly must declare exactly one `IModule` and remain loadable via `Assembly.Load`. Do not hand-author the host's `[Module]` attributes - they are generated.
- `EnforceExtendedAnalyzerRules=true` applies here, same as for generators.
- `IncludeBuildOutput=false` for all target projects - they only ship build assets, not assemblies.
- When adding a new toggle, also add it to every SKU csproj where it must be off-by-default; do not assume MSBuild default emptiness.
