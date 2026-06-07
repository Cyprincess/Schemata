# Advice Overview

The `Schemata.Advice` package provides the runtime infrastructure for the advisor pipeline: a zero-allocation dispatch token (`AdvicePipeline<TAdvisor>`), a static entry point (`Advisor.For<TAdvisor>()`), and a family of `AdviceRunner` static classes that resolve, sort, and execute advisors from the DI container. The `Schemata.Advice.Generator` package emits the `RunAsync` extension methods that connect the dispatch token to the correct runner at compile time.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Advice` | `AdvicePipeline.cs`, `Advisor.cs` |
| `Schemata.Advice` | `AdviceRunner\`2.cs` .. `AdviceRunner\`17.cs` (arities 1..16) |
| `Schemata.Advice.Generator` | `AdvicePipelineGenerator.cs`, `AdvisorInterfaceInfo.cs` |
| `Schemata.Abstractions` | `Advisors/IAdvisor.cs`, `Advisors/AdviceContext.cs`, `Advisors/AdviseResult.cs` |

## AdvicePipeline and Advisor

`AdvicePipeline<TAdvisor>` is a zero-size `readonly struct`. It carries no state and causes no heap allocation. Its sole purpose is to carry the generic parameter `TAdvisor` so that source-generated `RunAsync` extension methods can dispatch to the correct `AdviceRunner<...>` overload without ambiguity.

`Advisor` is the static entry point:

```csharp
public static class Advisor
{
    public static AdvicePipeline<TAdvisor> For<TAdvisor>()
        where TAdvisor : IAdvisor
        => default;
}
```

Calling `Advisor.For<IRepositoryAddAdvisor<Student>>()` returns a zero-size struct. The source-generated `RunAsync` extension method on that struct then delegates to `AdviceRunner<IRepositoryAddAdvisor<Student>, IRepository<Student>, Student>.RunAsync(...)`.

## Short-circuit semantics

The runner resolves all DI-registered implementations of `TAdvisor`, sorts them by `Order` ascending, and invokes each in sequence. The chain stops on the first non-`Continue` result:

| Result | Meaning |
| --- | --- |
| `Continue` | Proceed to the next advisor. If this is the last advisor, the operation executes normally. |
| `Block` | Abort the operation. No further advisors execute. |
| `Handle` | The advisor has fully handled the operation. No further advisors execute. Callers use whatever state the advisor placed in `AdviceContext`. |

`Block` and `Handle` both short-circuit, but callers interpret them differently. `Block` typically means "deny the operation silently." `Handle` means "I completed the operation; use my result." `AdviceCreateRequestIdempotency` is the canonical case: it returns `Handle` when it finds a cached response in the idempotency store, and the resource handler picks up the cached `CreateResultBase<TDetail>` from `AdviceContext`.

## AdviceContext

`AdviceContext` is a typed property bag keyed by `RuntimeTypeHandle`. It flows through the entire pipeline and gives advisors shared state and access to the service provider. See [Advice Pipeline](../core/advice-pipeline.md) for the full API.

Two `AdviceContext` instances coexist per resource request: one inside the resource operation handler and one inside `IRepository<T>.AdviceContext`. They never share state by design — `repository.SuppressQuerySoftDelete()` sets a marker only on the repository's context.

## Registering advisors

Advisors are registered via `TryAddEnumerable` with a `ServiceDescriptor.Scoped` descriptor. This ensures each implementation type is registered at most once while allowing multiple different advisor types for the same interface.

```csharp
// Closed-type registration
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());

// Open-generic registration
services.TryAddEnumerable(
    ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddConcurrency<>)));
```

## Order constants

`SchemataConstants.Orders` defines three anchor constants used by built-in advisors:

| Constant | Value | Usage |
| --- | --- | --- |
| `Base` | 100,000,000 | Starting point for most built-in advisors |
| `Extension` | 400,000,000 | Starting point for extension feature advisors |
| `Max` | 900,000,000 | Terminal advisors (soft-delete, concurrency, response idempotency) |

Built-in advisors chain by adding 10,000,000 increments from an anchor. Custom advisors can use any value that falls between the built-in increments or outside the reserved range.

## Design motivation

The zero-size struct dispatch token avoids heap allocation on every pipeline invocation. The source generator eliminates boilerplate `RunAsync` overloads — adding a new advisor interface requires only the interface declaration; the generator emits the extension method automatically. The `AdviceContext` typed bag avoids parameter explosion as pipelines grow more complex.

## See also

- [Advice Runtime](runtime.md) — `AdviceRunner` arity 1..16, no zero-argument specialization
- [Advice Generator](generator.md) — `AdvicePipelineGenerator` emission rules and gotchas
- [Advice Pipeline](../core/advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
- [Built-in Features](../core/built-in-features.md) — priority table
