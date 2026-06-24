# generators/

Roslyn source generators. **Always `netstandard2.0`** so Roslyn can load them.

```
Schemata.Advice.Generator/      230 LOC, 2 files - emits per-target advisor dispatch glue
Schemata.Modeling.Generator/    1.3k LOC, 36 files - `.skm` DSL → C# entities (parser uses Parlot)
```

## Packaging

Both generators ship as analyzers under `analyzers/dotnet/cs`:

- `Schemata.Advice` ships a `buildTransitive/Schemata.Advice.props` that auto-imports the analyzer for any consumer of the `Schemata.Advice` package. Source projects under `src/` also reference it via `Directory.Build.props` (suppress with `SchemataSkipGenerators=true`).
- `Schemata.Modeling.Generator` packs both `Schemata.Modeling.Generator.dll` and `Parlot.dll` into the analyzer slot. The meta-target packages include the modeling generator only when `UseDSLTargets=true`.

## Conventions

- `EnforceExtendedAnalyzerRules=true` is set by `Directory.Build.props` for everything under `generators/` (and `targets/`); APIs used here must be in the analyzer-safe surface.
- Output paths in artifacts: `artifacts/bin/<name>/<config>/netstandard2.0/<name>.dll`. Target packages reference these explicitly - keep the path stable.
- `IsPackable=true`, `IsShipping=true`, `SignAssembly=true`, `IncludeBuildOutput=false`. Do not change these for a generator project.
- Polyfills (`Schemata.Modeling.Generator/Polyfills/`) exist because `netstandard2.0` lacks newer APIs; prefer extending the polyfill set over re-targeting.

## Testing

`tests/Schemata.Modeling.Generator.Tests/` project-references the generator directly to exercise emission. No corresponding test project for `Schemata.Advice.Generator` - its emission is verified indirectly through downstream runtime tests in `tests/Schemata.Authorization.*` and others.

## Rules

- Generators MUST stay on `netstandard2.0`. Do not multi-target.
- `Microsoft.CodeAnalysis.*` packages are centrally pinned in [Directory.Packages.props](file:///D:/source/repos/Cyprin/Schemata/Directory.Packages.props). Roslyn version determines compatible host SDKs - bump deliberately.
- Never reference a `src/` runtime project from a generator - it would pull `net8.0/net10.0` assemblies into the analyzer load context.
