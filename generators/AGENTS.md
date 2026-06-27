# generators — Roslyn Source Generators

Two `IIncrementalGenerator` projects. Both target `netstandard2.0` (Roslyn loader requirement), pack as analyzer payloads (`analyzers/dotnet/cs`), and ship as separate NuGet packages.

## Packages

### Schemata.Advice.Generator

Emits the advisor-pipeline glue.

- **Entry**: [Schemata.Advice.Generator/AdvicePipelineGenerator.cs](Schemata.Advice.Generator/AdvicePipelineGenerator.cs) (`[Generator(LanguageNames.CSharp)]` + `IIncrementalGenerator`).
- **Driver inputs**:
  - syntax provider for interface declarations whose `BaseList` names `IAdvisor`;
  - compilation check that `Schemata.Advice.AdvicePipeline\`1` is in scope.
- **Output**: a partial static class `Schemata.Advice.AdvicePipelineExtensions` with one `RunAsync<...>(...)` overload per `IAdvisor<...>` arity, each forwarding to the matching `AdviceRunner<...>.RunAsync(...)`.
- **Driving types**: [src/Schemata.Abstractions/Advisors/IAdvisor.cs](../src/Schemata.Abstractions/Advisors/IAdvisor.cs), [AdviceContext](../src/Schemata.Abstractions/Advisors/AdviceContext.cs), [AdviseResult](../src/Schemata.Abstractions/Advisors/AdviseResult.cs), [AdvicePipeline](../src/Schemata.Advice/AdvicePipeline.cs).

### Schemata.Modeling.Generator

Compiles `.skm` files (Schemata Modeling Language) into C# entities, traits, enums.

- **Entry**: [Schemata.Modeling.Generator/Generator.cs](Schemata.Modeling.Generator/Generator.cs).
- **Driver inputs**: `AdditionalTextsProvider` filtered to files with `.skm` extension. Parser is hand-rolled with [Parlot](https://www.nuget.org/packages/Parlot) ([Schemata.Modeling.Generator/Parser.cs](Schemata.Modeling.Generator/Parser.cs)).
- **Output** (`obj/`, picked up at compile):
  - C# `record` per `Entity` block + nested `record` per `Object` block — emitted by [Generators/EntityGenerator.cs](Schemata.Modeling.Generator/Generators/EntityGenerator.cs).
  - C# `interface I{Name}` per `Trait` block — [Generators/TraitGenerator.cs](Schemata.Modeling.Generator/Generators/TraitGenerator.cs).
  - C# `enum` per `Enum` block — [Generators/EnumGenerator.cs](Schemata.Modeling.Generator/Generators/EnumGenerator.cs).
- **Grammar reference**: [Schemata.Modeling.Generator/README.md](Schemata.Modeling.Generator/README.md), [docs/modeling/](../docs/modeling/).

## How Consumers Wire Them

| Mechanism | Where | Effect |
|---|---|---|
| **Repo-wide `ProjectReference`** | [../Directory.Build.props](../Directory.Build.props#L90-L94) | Every `src/*` project gets `Schemata.Advice.Generator` with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`. Skip with `-p:SchemataSkipGenerators=true`. |
| **`buildTransitive` analyzer import** | [../src/Schemata.Advice/buildTransitive/Schemata.Advice.props](../src/Schemata.Advice/buildTransitive/Schemata.Advice.props) | Downstream consumers of `Schemata.Advice` pick up the advice generator automatically. |
| **Packed analyzer asset in target packages** | [../targets/](../targets/) | `Schemata.{App,Business,Module}.Complex.Targets` pack the modeling generator so `.skm` files inside consumer projects compile. |

`AdditionalFiles` for `.skm` are configured by the complex target packages — a plain class library + the modeling generator alone will NOT discover `.skm`; you need the target package or an explicit `<AdditionalFiles Include="**/*.skm" />`.

## Conventions

- **Use `IIncrementalGenerator`, not `ISourceGenerator`** for any new generator added here. Wire through the same `[Generator(LanguageNames.CSharp)]` attribute.
- **`netstandard2.0` only** — do not multi-target. Roslyn loads analyzers in a netstandard2.0 context.
- **`EnforceExtendedAnalyzerRules=true`** is on for everything under `generators/` and `targets/` ([../Directory.Build.props](../Directory.Build.props#L67-L69)); RS-prefixed rule violations break the build.

## Anti-Patterns

- **Do NOT** add `System.*` package references that require newer-than-netstandard2.0 surface.
- **Do NOT** read files via `File.ReadAllText` — always go through `AdditionalText` / `IncrementalValuesProvider` so caching works.
- **Do NOT** rely on `compilation.GetSymbolsWithName` for hot paths — prefer the syntax provider with a predicate, then a transform that resolves symbols only for matches.
- **Do NOT** emit XML doc comments from the generator (analyzers running in netstandard2.0 cannot reliably escape user strings); leave docs in hand-written partial files when needed.

## Notes

- Both generators ship with their own `README.md` displayed on nuget.org.
- The advice generator output is committed to the build cache only — there is no checked-in expected snapshot. Test coverage for the modeling generator lives at [../tests/Schemata.Modeling.Generator.Tests/](../tests/Schemata.Modeling.Generator.Tests/).
- `Schemata.Modeling.Generator.csproj` packs Parlot into `analyzers/dotnet/cs` alongside the generator DLL; the consumer never sees Parlot at runtime.
