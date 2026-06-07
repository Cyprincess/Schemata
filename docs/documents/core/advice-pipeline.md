# Advice Pipeline

The advice pipeline is Schemata's mechanism for injecting cross-cutting concerns into operations at well-defined interception points. Advisors are small, focused units of logic that run in sequence before, during, or after an operation. Each advisor can inspect and modify state, allow the operation to continue, or short-circuit the pipeline entirely. The pipeline is the primary extension point for repository mutations, resource operations, validation, and authorization.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Abstractions` | `Advisors/IAdvisor.cs` (arities 1..16), `Advisors/AdviceContext.cs`, `Advisors/AdviseResult.cs` |
| `Schemata.Advice` | `AdvicePipeline.cs`, `Advisor.cs`, `AdviceRunner\`2.cs` .. `AdviceRunner\`17.cs` |
| `Schemata.Advice.Generator` | `AdvicePipelineGenerator.cs` — emits `RunAsync` extension methods |

## IAdvisor

All advisors implement the marker interface `IAdvisor`, which defines a single property:

```csharp
public interface IAdvisor
{
    int Order { get; }
}
```

`Order` controls the sequence in which advisors execute within a pipeline. Lower values run first.

### Generic variants

`IAdvisor<T1>` through `IAdvisor<T1, ..., T16>` extend `IAdvisor` and declare `AdviseAsync`. The arity matches the number of arguments the advisor receives alongside the `AdviceContext`. There is no zero-argument specialization; the minimum arity is 1.

```csharp
public interface IAdvisor<in T1> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default);
}

public interface IAdvisor<in T1, in T2> : IAdvisor
{
    Task<AdviseResult> AdviseAsync(AdviceContext ctx, T1 a1, T2 a2, CancellationToken ct = default);
}

// ... up to IAdvisor<T1, T2, ..., T16>
```

Every type parameter is declared `in` (contravariant), so an advisor registered against a base type matches pipelines that pass a derived type.

## AdviceContext

`AdviceContext` is a typed property bag that flows through the entire pipeline, giving advisors shared state and access to the service provider.

```csharp
public class AdviceContext
{
    public AdviceContext(IServiceProvider sp);

    public IServiceProvider ServiceProvider { get; }

    public void Set<T>(T? value);
    public T?   Get<T>();
    public bool TryGet<T>(out T? value);
    public bool Has<T>();
}
```

Values are keyed by `RuntimeTypeHandle`, so each type can store exactly one value. This makes `AdviceContext` a lightweight alternative to passing many parameters through the pipeline.

| Method | Behavior |
| --- | --- |
| `Set<T>(value)` | Stores `value` keyed by `typeof(T)`. Overwrites any previous value of the same type. |
| `Get<T>()` | Returns the stored value or throws `KeyNotFoundException`. |
| `TryGet<T>(out value)` | Returns `true` and sets `value` when a non-null entry exists; otherwise returns `false`. |
| `Has<T>()` | Returns `true` if an entry for `typeof(T)` exists, even if the stored value is `null`. |

`ServiceProvider` allows advisors to resolve additional services at execution time without constructor injection.

## AdviseResult

Every `AdviseAsync` call returns one of three `AdviseResult` values:

```csharp
public enum AdviseResult
{
    Continue,
    Block,
    Handle,
}
```

| Result | Meaning |
| --- | --- |
| `Continue` | Proceed to the next advisor. If this is the last advisor, the operation executes normally. |
| `Block` | Abort the operation. No further advisors execute. Callers treat this as a silent refusal or return a default value. |
| `Handle` | The advisor has fully handled the operation (for example, returning a cached result or converting a delete into a soft-delete update). No further advisors execute. Callers use whatever state the advisor placed in the context. |

Both `Block` and `Handle` short-circuit the pipeline. The distinction lets callers decide how to interpret the early exit.

## Pipeline execution

### AdviceRunner

The `AdviceRunner<TAdvisor, T1, ..., TN>` static classes contain the execution loop. There is one class per arity, covering arities 1 through 16 (`AdviceRunner\`2.cs` through `AdviceRunner\`17.cs`). Every runner follows the same algorithm:

```csharp
public static async Task<AdviseResult> RunAsync(AdviceContext ctx, T1 a1, CancellationToken ct = default)
{
    var advisors = ctx.ServiceProvider.GetServices<TAdvisor>().OrderBy(a => a.Order).ToList();
    foreach (var advisor in advisors)
    {
        ct.ThrowIfCancellationRequested();
        var result = await advisor.AdviseAsync(ctx, a1, ct);
        if (result is not AdviseResult.Continue)
        {
            return result;
        }
    }

    return AdviseResult.Continue;
}
```

Key points:

1. **Resolution** — advisors are resolved from DI via `GetServices<TAdvisor>()`, which returns all registrations for the advisor interface.
2. **Ordering** — the resolved advisors are sorted by `Order` ascending. Advisors with the same `Order` value run in registration order.
3. **Short-circuiting** — the loop stops on the first non-`Continue` result and propagates it to the caller.
4. **Cancellation** — `CancellationToken` is checked before each advisor executes.

### Advisor entry point

Callers create a pipeline through the `Advisor` static class:

```csharp
public static class Advisor
{
    public static AdvicePipeline<TAdvisor> For<TAdvisor>() where TAdvisor : IAdvisor
        => default;
}
```

`AdvicePipeline<TAdvisor>` is a zero-size struct used purely as a dispatch token for source-generated extension methods. It carries no state and causes no heap allocation. The call site reads:

```csharp
var result = await Advisor.For<IRepositoryAddAdvisor<Student>>()
                          .RunAsync(ctx, repository, entity, ct);
```

The `RunAsync` extension method is emitted by `Schemata.Advice.Generator` — see [Generator](../advice/generator.md).

### How callers consume the result

Repository and resource operations typically switch on the result:

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

Some pipelines interpret the three results differently. Query pipelines treat `Handle` as "use the result already placed in the context" and `Block` as "return the default value."

## Registering advisors

Advisors are registered in the DI container using `TryAddEnumerable` with a `ServiceDescriptor.Scoped` descriptor. This ensures each implementation type is registered at most once while allowing multiple different advisor types for the same interface.

**Closed-type registration** (concrete advisor targeting a specific entity):

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped<IRepositoryAddAdvisor<Student>, StudentNameAdvisor>());
```

**Open-generic registration** (advisor applying to all entities):

```csharp
services.TryAddEnumerable(
    ServiceDescriptor.Scoped(typeof(IRepositoryAddAdvisor<>), typeof(AdviceAddConcurrency<>)));
```

## State markers

Advisors communicate suppression state through empty marker classes stored in `AdviceContext`. The fluent method is a verb (`SuppressSoftDelete()`); the marker class it sets is a state noun (`SoftDeleteSuppressed`). The advisor checks `ctx.Has<SoftDeleteSuppressed>()` at the top of `AdviseAsync` and returns `Continue` immediately if the marker is present.

```csharp
public sealed class SoftDeleteSuppressed;

public sealed class AdviceAddSoftDelete<TEntity> : IRepositoryAddAdvisor<TEntity>
{
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, ...) {
        if (ctx.Has<SoftDeleteSuppressed>()) return Task.FromResult(AdviseResult.Continue);
        // ...
    }
}
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

The advisor pattern separates cross-cutting concerns from the core operation logic. Adding a new behavior (timestamps, soft-delete, idempotency) requires only a new advisor class and a `TryAddEnumerable` registration. The operation handler never changes. The `Order` property provides a single axis for sequencing without requiring explicit dependency declarations between advisors.

## Caveats

- Advisors are resolved from DI on every pipeline execution. Scoped advisors get a fresh instance per request; singleton advisors are shared. Avoid mutable state in singleton advisors.
- `AdviceContext` is not thread-safe. Do not share a single context across concurrent pipeline executions.
- Two `AdviceContext` instances coexist per resource request: one inside the handler and one inside `IRepository<T>.AdviceContext`. They never share state by design.

## See also

- [Advice Overview](../advice/overview.md) — `AdvicePipeline`, `AdviceRunner`, short-circuit semantics
- [Advice Runtime](../advice/runtime.md) — `AdviceRunner` arity 1..16
- [Advice Generator](../advice/generator.md) — `AdvicePipelineGenerator` emission rules
- [Feature System](feature-system.md) — `Order` vs `Priority` for features
- [Built-in Features](built-in-features.md) — priority table
