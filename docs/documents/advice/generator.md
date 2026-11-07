# Advice Source Generator

`Schemata.Advice.Generator` emits `RunAsync` extensions for advisor interfaces and the
`AdviceRunner` family used by those extensions. It targets `netstandard2.0` and is included as an
analyzer for `src/` projects through `Directory.Build.props`.

## Source ownership

| Concern | Executable source |
| --- | --- |
| Discovery, semantic matching, emission | `generators/Schemata.Advice.Generator/AdvicePipelineGenerator.cs` |
| Emission metadata | `generators/Schemata.Advice.Generator/AdvisorInterfaceInfo.cs` |
| Runtime token | `src/Schemata.Advice/AdvicePipeline.cs`, `src/Schemata.Advice/Advisor.cs` |
| Advisor contracts | `src/Schemata.Abstractions/Advisors/IAdvisor.cs` |

## Activation

`Initialize` probes for `Schemata.Advice.AdvicePipeline` with metadata arity 1. A compilation
referencing that type can receive per-interface extensions. `DefinesAdvicePipeline` additionally
compares the type's declaring assembly with the current compilation through
`SymbolEqualityComparer.Default`. Only the assembly declaring the pipeline receives the runners.
A compilation referencing only Abstractions receives neither output family.

`GenerateRunners` emits `AdviceRunner.Arity1.g.cs` through `AdviceRunner.Arity16.g.cs` into
`Schemata.Advice`. Each runner resolves `GetServices<TAdvisor>()` from the supplied
`AdviceContext.ServiceProvider`, orders by `Order`, checks cancellation before each invocation,
and returns the first result other than `Continue`. Completing the collection returns `Continue`.

## Candidate and semantic matching

The syntax filter accepts every interface declaration with a base list. Aliases and inherited
advisor interfaces therefore reach semantic analysis without requiring the text `IAdvisor`.
`GetAdvisorInfo` resolves the declared symbol and the non-generic marker
`Schemata.Abstractions.Advisors.IAdvisor` from the compilation.

`IsAdvisorInterface` accepts a generic interface when its original definition directly inherits
the resolved marker, comparing symbols through `SymbolEqualityComparer.Default`. Matching a
similarly named type in another namespace is insufficient.

Selection takes the first matching entry from `AllInterfaces`, then replaces it with the first
matching direct base from `Interfaces`, if present. Each loop stops at its first match. An
interface with several advisor bases therefore has one selected argument list, rather than one
extension per base.

## Emitted signature

The generated extension receives the pipeline token, `AdviceContext`, the selected advisor's
arguments in declaration order, and an optional `CancellationToken`. It forwards those arguments
to the corresponding runner. Repository add, update, and remove contracts include
`MutationOptions` before cancellation; the emitted signature carries that argument too.

Containing-type and interface type parameters are forwarded with their constraints. Repeated
parameter names are disambiguated and C# keywords are escaped. Type references use fully qualified
names with nullable annotations. Public reachable interfaces produce public extensions; internal
accessibility caps the extension at internal. An interface hidden behind private/protected
accessibility produces diagnostic `SKA001` instead of an inaccessible extension.

## Stable hint identity

`BuildSymbolKeyName` includes the type kind, namespace, containing types and their metadata
arities, interface name, generic arity, and parameter names. Each segment is UTF-8 encoded with a
length prefix and hexadecimal bytes. The resulting key supplies both deduplication identity and
the `.g.cs` hint name. Partial declarations collapse to one output; identical simple names in
different namespaces or containing types retain distinct keys.

## Extension points and limits

Define an interface inheriting a supported `IAdvisor<T1, …, TN>` contract to receive an extension
on the next build. `MaxArity` is 16 and must track the available contract and runner overloads.
`SchemataSkipGenerators=true` disables the analyzer reference supplied by the repository props.

This reference was traced against the generator implementation. Its generated behavior is tested
by `tests/Schemata.Advice.Generator.Tests`; those tests were not executed during this documentation
alignment.

## See also

- [Advice Overview](overview.md)
- [Advice Runtime](runtime.md)
- [Advice Pipeline](../core/advice-pipeline.md)
