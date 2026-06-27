# Schemata.Advice — Advisor Runtime

The cross-cutting mechanism for the whole framework. Every CRUD call, HTTP resource request, user registration, flow transition runs through an ordered chain of `IAdvisor`. New cross-cutting behaviour is added by registering an advisor, never by subclassing.

## Layout

```
Schemata.Advice/
├── AdvicePipeline.cs              # the IAdvisor<...> generic the runtime + generator key on
├── Advisor.cs                     # static helpers (Advisor.For<TAdvisor>())
├── AdviceRunner.cs                # base + reflection plumbing
├── AdviceRunner`{2..17}.cs        # one per arity, drives the generated RunAsync
└── buildTransitive/
    └── Schemata.Advice.props      # imports the analyzer for downstream packages
```

The advisor *contract* itself (`IAdvisor`, `AdviceContext`, `AdviseResult`) lives in [Schemata.Abstractions/Advisors/](../Schemata.Abstractions/Advisors/). This package is only the runtime + generator hook.

## How Generation Plugs In

- [generators/Schemata.Advice.Generator](../../generators/Schemata.Advice.Generator/) scans every interface that derives from `IAdvisor` and, for each compilation that references `AdvicePipeline<>`, emits a static `Schemata.Advice.AdvicePipelineExtensions.RunAsync<...>(...)` overload that drives the matching `AdviceRunner<...>`.
- The generator is wired into every `src/*` project by [../../Directory.Build.props](../../Directory.Build.props#L90-L94). Downstream consumers pick it up via [buildTransitive/Schemata.Advice.props](buildTransitive/Schemata.Advice.props).
- Disable with `-p:SchemataSkipGenerators=true` (used by [docs/docfx.json](../../docs/docfx.json) so DocFX metadata generation does not invoke the analyzer).

## Runner Arities

`AdviceRunner.cs` holds the abstract base + reflection cache. `AdviceRunner\`2..17.cs` are one class per number of pipeline parameters (excluding the context). Add a new arity only if a new advisor interface needs more parameters than the existing files cover — that is rare; consider packing parameters into a single record first.

## Conventions

- **An advisor is a one-method class** implementing `IAdvisor<...>.AdviseAsync(AdviceContext, ...args, CancellationToken)`. Return `AdviseResult.Continue` / `AdviseResult.Break` (or the typed variants).
- **Register through DI**: `services.AddTransient<IAdvisor<...>, MyAdvisor>()`. Multiple advisors for the same interface fire in registration order.
- **Use `Advisor.For<TAdvisor>()`** as a token when constructing pipelines manually — keeps the type system aligned with the generated extensions.
- **`AdviceContext` carries per-call state**. Treat it as a scratchpad for one invocation, never as durable storage.

## Anti-Patterns

- **Do NOT** share an `AdviceContext` across concurrent pipelines or threads ([docs/documents/core/advice-pipeline.md:223-225](../../docs/documents/core/advice-pipeline.md)).
- **Do NOT** mutate `AdviseResult` instances; treat them as immutable.
- **Do NOT** call into `AdviceRunner<>` directly from feature code — use the generated `AdvicePipelineExtensions.RunAsync<...>` overload.
- **Do NOT** add async-void hooks. Every advisor returns `Task<AdviseResult>`; swallow nothing.
- **Do NOT** put thread-affine resources (DbContext, HttpContext) into long-lived advisor fields. Advisors are typically transient.

## Notes

- The generator output is the user-visible API. If you rename `AdviceRunner` types, the generated extensions break — coordinate the rename across [generators/Schemata.Advice.Generator/AdvicePipelineGenerator.cs](../../generators/Schemata.Advice.Generator/AdvicePipelineGenerator.cs).
- `AdvicePipeline<TAdvisor>` is a marker the generator looks for; deleting it silently disables generation.
- Sample interfaces driving generation: `IRepositoryAdvisor<...>`, `IResourceAdvisor<...>`, `IIdentityAdvisor<...>`, `IFlowAdvisor<...>`, `IEventAdvisor<...>`.
