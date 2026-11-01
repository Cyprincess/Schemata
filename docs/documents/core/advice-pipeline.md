# Advice Pipeline

The advice pipeline injects cross-cutting logic at well-defined interception points. An advisor is
a focused unit that runs in sequence; it reads and rewrites its typed payload, lets the operation
continue, or short-circuits the chain. The pipeline backs repository mutations, resource
operations, validation, and authorization.

## Where the code lives

| Package                     | Key files                                                                                       |
| --------------------------- | ----------------------------------------------------------------------------------------------- |
| `Schemata.Abstractions`     | `Advisors/IAdvisor.cs` (arities 1..16), `Advisors/AdviceContext.cs`, `Advisors/AdviseResult.cs` |
| `Schemata.Advice`           | `AdvicePipeline.cs`, `Advisor.cs`                                                               |
| `Schemata.Advice.Generator` | `AdvicePipelineGenerator.cs` — emits the `RunAsync` extension methods and the runner family     |

The `AdviceRunner` family has no source files; the generator emits it into the `Schemata.Advice`
compilation.

## IAdvisor

Every advisor implements the marker interface `IAdvisor`:

```csharp
public interface IAdvisor
{
    int Order { get; }
}
```

`Order` sets execution sequence within a pipeline; lower runs first.

### Generic variants

`IAdvisor<T1>` through `IAdvisor<T1, ..., T16>` extend `IAdvisor` and declare `AdviseAsync`. The
arity matches the number of domain arguments passed alongside the `AdviceContext`; the minimum is

1.

```csharp
public interface IAdvisor<in T1> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default);
}

public interface IAdvisor<in T1, in T2> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(AdviceContext ctx, T1 a1, T2 a2, CancellationToken ct = default);
}
// ... up to IAdvisor<T1, ..., T16>
```

Every type parameter is `in` (contravariant), so an advisor registered against a base type matches
pipelines that pass a derived type.

## AdviceContext

`AdviceContext` carries the `IServiceProvider` of the pipeline that constructed it. It holds no
per-request state: each advisor stage receives its payload as an explicit parameter. A stage that
short-circuits writes its result to the stage's typed context: repository query advisors use
`QueryContext<TEntity,TResult,T>.Result`, authorization advisors use their context's `Result`, and
stages that declare a handled-output carrier use `HandledResponse<TResponse>` /
`IHandledResponse<TResponse>`, read back by `HandledResponseRunner` from `Schemata.Abstractions/Advisors`.
Resource request and handler advisors, `ResourceMethodExecution`, and `IdentityOperationExecution` are
examples. Event publish `Handle` aborts the publish path and may set `EventContext.Result` that nothing
reads back; consume advisors run after dispatch and the bus ignores their `AdviseResult` (see
[Event dispatch pipeline](../event/dispatch-pipeline.md)).

```csharp
public class AdviceContext
{
    public AdviceContext(IServiceProvider sp);
    public IServiceProvider ServiceProvider { get; }
}
```

`ServiceProvider` lets advisors resolve services at execution time without constructor injection.

## AdviseResult

`AdviseAsync` returns one of three values:

```csharp
public enum AdviseResult
{
    Continue,
    Block,
    Handle,
}
```

| Result     | Meaning                                                                                                                                                                                      |
| ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Continue` | Proceed to the next advisor. After the last advisor, the operation runs normally.                                                                                                            |
| `Block`    | Abort the operation; no further advisors run. The caller treats it as a silent refusal or default value.                                                                                     |
| `Handle`   | The advisor has fully handled the operation (e.g. a cached result, or a delete converted to a soft-delete). No further advisors run; the caller reads the stage's typed result — `QueryContext<TEntity,TResult,T>.Result`, an authorization context's `Result`, or `HandledResponse<TResponse>` when the stage declares a handled-output carrier. |

`Block` and `Handle` both short-circuit; the distinction lets the caller interpret the early exit.

## Pipeline execution

### AdviceRunner

The `AdviceRunner<TAdvisor, T1, ..., TN>` static classes hold the loop, one per arity (1..16).
Every runner runs the same algorithm:

```csharp
public static async Task<AdviseResult> RunAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default)
{
    var advisors = ctx.ServiceProvider.GetServices<TAdvisor>().OrderBy(a => a.Order).ToList();
    foreach (var advisor in advisors)
    {
        ct.ThrowIfCancellationRequested();
        var result = await advisor.AdviseAsync(ctx, a1, ct);
        if (result is not AdviseResult.Continue) return result;
    }

    return AdviseResult.Continue;
}
```

1. **Resolution** — advisors come from DI via `GetServices<TAdvisor>()`.
2. **Ordering** — sorted by `Order` ascending; ties run in DI registration order.
3. **Cancellation** — checked before each advisor.
4. **Short-circuit** — the loop returns the first non-`Continue` result.

### Advisor entry point

Callers open a pipeline through the `Advisor` static class:

```csharp
public static class Advisor
{
    public static AdvicePipeline<TAdvisor> For<TAdvisor>() where TAdvisor : IAdvisor
        => default;
}
```

`AdvicePipeline<TAdvisor>` is a zero-size `readonly struct` used as a dispatch token for the
source-generated extension methods. It allocates nothing. The call site reads:

```csharp
var result = await Advisor.For<IRepositoryAddAdvisor<Student>>()
                          .RunAsync(ctx, repository, entity, ct);
```

`Schemata.Advice.Generator` emits the `RunAsync` extension. Callers typically switch on the result:

```csharp
switch (await Advisor.For<IRepositoryAddAdvisor<TEntity>>()
                     .RunAsync(ctx, this, entity, ct))
{
    case AdviseResult.Block:
    case AdviseResult.Handle:
        return;
}
// Normal operation proceeds here
```

Query pipelines read `Handle` as "use `QueryContext<TEntity,TResult,T>.Result`" and `Block` as
"return the default value."

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

## Per-operation options

Advisors read per-call overrides from `QueryOptions` on a build-query container and from
`MutationOptions` on a mutation. Both records travel with the call, so the choice applies to that
operation alone and never reaches nested or parallel work.

```csharp
public sealed class AdviceAddSoftDelete<TEntity> : IRepositoryAddAdvisor<TEntity>
{
    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, IRepository<TEntity> repository, TEntity entity, MutationOptions options,
        CancellationToken ct) {
        if (options.IgnoreSoftDelete) return Task.FromResult(AdviseResult.Continue);
        // ...
    }
}
```

Framework-defined operational overrides belong in the option records; pipeline state travels in
the typed stage payloads. `IgnoreCacheEviction` is the
one commit-scoped option: it
does not change the call, it suppresses eviction for the next commit of the repository's current
unit of work, and both records declare it.

## Order constants

`SchemataConstants.Orders` anchors advisor ordering:

| Constant    | Value       | Usage                                                 |
| ----------- | ----------- | ----------------------------------------------------- |
| `Base`      | 100,000,000 | Starting point for most built-in advisors             |
| `Extension` | 400,000,000 | Starting point for extension advisors                 |
| `Max`       | 900,000,000 | Terminal advisors (soft-delete, response idempotency) |

Built-in repository advisors chain by 10M increments from `Base`. On the add pipeline:
`AdviceAddTimestamp` (100M), `AdviceAddConcurrency` (110M), `AdviceAddCanonicalName` (120M),
`AdviceAddValidation` (130M), `AdviceAddUniqueness` (140M), and `AdviceAddSoftDelete` (`Max`, 900M).

## Design rationale

The advisor pattern keeps cross-cutting concerns out of the operation handler. A new behavior is a
new advisor class plus a `TryAddEnumerable` registration; the handler never changes. `Order` gives
a single sequencing axis without explicit dependencies between advisors.

## Caveats

- Advisors resolve from DI on every pipeline run. Scoped advisors get a fresh instance per request;
  avoid mutable state in singletons.
- `AdviceContext` exposes only the pipeline's `IServiceProvider`; contexts are independent per pipeline.
- Two `AdviceContext` instances coexist per resource request — the handler's dispatch context and
  `IRepository<T>.AdviceContext` — each bound to its own provider.

## See also

- [Advice Overview](../advice/overview.md) — `AdvicePipeline`, `Advisor`, short-circuit semantics
- [Advice Runtime](../advice/runtime.md) — the `AdviceRunner` family, arity 1..16
- [Advice Generator](../advice/generator.md) — `AdvicePipelineGenerator` emission rules
