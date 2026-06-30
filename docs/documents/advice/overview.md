# Advice Overview

`Schemata.Advice` is the runtime for the advisor pipeline: a zero-allocation dispatch token
(`AdvicePipeline<TAdvisor>`), a static entry point (`Advisor.For<TAdvisor>()`), and a family of
`AdviceRunner` static classes that resolve, sort, and execute advisors from DI.
`Schemata.Advice.Generator` emits the `RunAsync` extension methods that bind the token to the
correct runner at compile time.

## Where the code lives

| Package                     | Key files                                                                       |
| --------------------------- | ------------------------------------------------------------------------------- |
| `Schemata.Advice`           | `AdvicePipeline.cs`, `Advisor.cs`                                               |
| `Schemata.Advice`           | the `AdviceRunner` family (arities 1..16)                                       |
| `Schemata.Advice.Generator` | `AdvicePipelineGenerator.cs`, `AdvisorInterfaceInfo.cs`                         |
| `Schemata.Abstractions`     | `Advisors/IAdvisor.cs`, `Advisors/AdviceContext.cs`, `Advisors/AdviseResult.cs` |

## AdvicePipeline and Advisor

`AdvicePipeline<TAdvisor>` is a zero-size `readonly struct`. It carries no state and allocates
nothing; it exists only to carry the `TAdvisor` type so a source-generated `RunAsync` extension
can dispatch to the right `AdviceRunner<...>`.

`Advisor` is the entry point:

```csharp
public static class Advisor
{
    public static AdvicePipeline<TAdvisor> For<TAdvisor>()
        where TAdvisor : IAdvisor
        => default;
}
```

`Advisor.For<IRepositoryAddAdvisor<Student>>()` returns the zero-size struct; the generated
`RunAsync` extension on it delegates to
`AdviceRunner<IRepositoryAddAdvisor<Student>, IRepository<Student>, Student>.RunAsync(...)`.

## Short-circuit semantics

The runner resolves all DI-registered `TAdvisor` implementations, sorts by `Order` ascending, and
invokes each in turn. The chain stops on the first non-`Continue` result:

| Result     | Meaning                                                                                                                 |
| ---------- | ----------------------------------------------------------------------------------------------------------------------- |
| `Continue` | Proceed to the next advisor; after the last, the operation runs normally.                                               |
| `Block`    | Abort the operation; no further advisors run.                                                                           |
| `Handle`   | The advisor handled the operation; no further advisors run, and the caller uses the state it placed in `AdviceContext`. |

`AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail>` is the canonical `Handle` case: on a
cache hit with a matching payload hash it returns `Handle` after storing a `CreateResultBase<TDetail>`
in the context, and the resource handler returns that cached result.

## AdviceContext

`AdviceContext` is a typed property bag keyed by `RuntimeTypeHandle`, flowing through the pipeline
with shared state and the service provider. See [Advice Pipeline](../core/advice-pipeline.md) for
the full API.

Two `AdviceContext` instances coexist per resource request: one in the resource operation handler,
one in `IRepository<T>.AdviceContext`. They never share state — `repository.SuppressQuerySoftDelete()`
sets a marker only on the repository's context.

## Registering advisors

Advisors register with `TryAddEnumerable` and a `ServiceDescriptor.Scoped` descriptor, so each
implementation type registers once while many advisor types share one interface.

```csharp
// Closed-type registration
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, AdviceAddStudentName>());

// Open-generic registration
services.TryAddEnumerable(
    ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddConcurrency<>)));
```

## Order constants

`SchemataConstants.Orders` anchors advisor ordering:

| Constant    | Value       | Usage                                                 |
| ----------- | ----------- | ----------------------------------------------------- |
| `Base`      | 100,000,000 | Starting point for most built-in advisors             |
| `Extension` | 400,000,000 | Starting point for extension advisors                 |
| `Max`       | 900,000,000 | Terminal advisors (soft-delete, response idempotency) |

Built-in advisors chain by 10M increments from an anchor. Custom advisors take a value between the
built-in steps or outside the reserved range.

## Design rationale

The zero-size struct token avoids heap allocation on every pipeline invocation. The generator
removes the boilerplate `RunAsync` overloads — a new advisor interface needs only its declaration.
The `AdviceContext` typed bag avoids parameter explosion as pipelines grow.

## See also

- [Advice Runtime](runtime.md) — the `AdviceRunner` family, arity 1..16
- [Advice Generator](generator.md) — `AdvicePipelineGenerator` emission rules
- [Advice Pipeline](../core/advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
