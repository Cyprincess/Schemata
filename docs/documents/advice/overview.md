# Advice Overview

`Schemata.Advice` is the runtime for the advisor pipeline: a zero-allocation dispatch token
(`AdvicePipeline<TAdvisor>`), a static entry point (`Advisor.For<TAdvisor>()`), and a family of
`AdviceRunner` static classes that resolve, sort, and execute advisors from DI.
`Schemata.Advice.Generator` emits the `RunAsync` extension methods that bind the token to the
correct runner at compile time, and the `AdviceRunner` classes themselves.

## Where the code lives

| Package                     | Key files                                                                       |
| --------------------------- | ------------------------------------------------------------------------------- |
| `Schemata.Advice`           | `AdvicePipeline.cs`, `Advisor.cs`                                               |
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
| `Handle`   | The advisor handled the operation; no further advisors run, and the caller reads the stage's typed result — `QueryContext<TEntity,TResult,T>.Result`, an authorization context's `Result`, or `HandledResponse<TResponse>` when the stage declares a handled-output carrier. |

A stage carries its short-circuit result on its own typed context. Repository query advisors write
`QueryContext<TEntity,TResult,T>.Result`; authorization advisors write their authorization context's
`Result`. Any stage that declares a handled-output carrier writes `HandledResponse<TResponse>` /
`IHandledResponse<TResponse>`, and the owning handler reads it back through `HandledResponseRunner`
(`Schemata.Abstractions/Advisors/HandledResponseRunner.cs`). Examples: the resource create, update,
delete, and get request and handler advisors; custom-method stages carried by `ResourceMethodExecution`;
and Identity operations carried by `IdentityOperationExecution` (`SchemataSignInManager` and
`IdentityOperationHandler`). The resource create idempotency wrap is the exception: it returns the
replayed `CreateResultBase<TDetail>` directly and never writes the carrier (see
[Resource create pipeline](../resource/create-pipeline.md)).

Event `Handle` is different: a publish advisor that returns `Handle` aborts the publish path — no
lifecycle observers or audit row, no subscription matching or delivery — and may set
`EventContext.Result`, which nothing reads back. Consume advisors run after handler dispatch, and the
bus ignores their `AdviseResult`; see [Event dispatch pipeline](../event/dispatch-pipeline.md).

## AdviceContext

`AdviceContext` carries the `IServiceProvider` of the pipeline that constructed it and holds no
state; advisor stages receive typed payloads as parameters. See
[Advice Pipeline](../core/advice-pipeline.md) for the constructor contract.

Two `AdviceContext` instances coexist per resource request: the handler's dispatch context and
`IRepository<T>.AdviceContext`. Each is bound to its own provider, and an option passed to a
repository call affects only that call's pipeline.

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
Explicit typed payloads on each advisor stage avoid parameter explosion as pipelines grow.

## See also

- [Advice Runtime](runtime.md) — the `AdviceRunner` family, arity 1..16
- [Advice Generator](generator.md) — `AdvicePipelineGenerator` emission rules
- [Advice Pipeline](../core/advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
