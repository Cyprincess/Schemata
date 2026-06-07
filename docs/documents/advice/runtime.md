# Advice Runtime

The advice runtime is the set of static `AdviceRunner` classes that execute the advisor chain-of-responsibility. Each class covers one arity of the `IAdvisor<T1, ..., TN>` generic family. The runtime supports arities **1 through 16**. There is no zero-argument specialization — every advisor receives at least one domain argument in addition to `AdviceContext` and `CancellationToken`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Advice` | `AdviceRunner\`2.cs` (arity 1) through `AdviceRunner\`17.cs` (arity 16) |
| `Schemata.Advice` | `AdvicePipeline.cs`, `Advisor.cs` |
| `Schemata.Abstractions` | `Advisors/IAdvisor.cs`, `Advisors/AdviceContext.cs`, `Advisors/AdviseResult.cs` |

The file naming follows the CLR generic arity convention: `AdviceRunner\`2.cs` has two type parameters (`TAdvisor` + `T1`), `AdviceRunner\`17.cs` has seventeen (`TAdvisor` + `T1..T16`).

## AdviceRunner family

Each `AdviceRunner<TAdvisor, T1, ..., TN>` is a static class with a single `RunAsync` method. The class constraint on `TAdvisor` matches the corresponding `IAdvisor<T1, ..., TN>` interface.

### Arity 1 — AdviceRunner\`2

```csharp
public static class AdviceRunner<TAdvisor, T1>
    where TAdvisor : IAdvisor<T1>
{
    public static async Task<AdviseResult> RunAsync(
        AdviceContext     ctx,
        T1                a1,
        CancellationToken ct = default)
    {
        var advisors = ctx.ServiceProvider.GetServices<TAdvisor>()
                                          .OrderBy(a => a.Order)
                                          .ToList();
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
}
```

All higher-arity runners follow the same pattern, adding `T2 a2`, `T3 a3`, and so on up to `T16 a16`.

### Supported arities

| File | Type parameters | Advisor interface matched |
| --- | --- | --- |
| `AdviceRunner\`2.cs` | `TAdvisor, T1` | `IAdvisor<T1>` |
| `AdviceRunner\`3.cs` | `TAdvisor, T1, T2` | `IAdvisor<T1, T2>` |
| `AdviceRunner\`4.cs` | `TAdvisor, T1, T2, T3` | `IAdvisor<T1, T2, T3>` |
| `AdviceRunner\`5.cs` | `TAdvisor, T1, T2, T3, T4` | `IAdvisor<T1, T2, T3, T4>` |
| `AdviceRunner\`6.cs` | `TAdvisor, T1, T2, T3, T4, T5` | `IAdvisor<T1, T2, T3, T4, T5>` |
| `AdviceRunner\`7.cs` | `TAdvisor, T1, T2, T3, T4, T5, T6` | `IAdvisor<T1, T2, T3, T4, T5, T6>` |
| `AdviceRunner\`8.cs` | `TAdvisor, T1, T2, T3, T4, T5, T6, T7` | `IAdvisor<T1, T2, T3, T4, T5, T6, T7>` |
| `AdviceRunner\`9.cs` | `TAdvisor, T1, T2, T3, T4, T5, T6, T7, T8` | `IAdvisor<T1, T2, T3, T4, T5, T6, T7, T8>` |
| `AdviceRunner\`10.cs` | `TAdvisor, T1..T9` | `IAdvisor<T1..T9>` |
| `AdviceRunner\`11.cs` | `TAdvisor, T1..T10` | `IAdvisor<T1..T10>` |
| `AdviceRunner\`12.cs` | `TAdvisor, T1..T11` | `IAdvisor<T1..T11>` |
| `AdviceRunner\`13.cs` | `TAdvisor, T1..T12` | `IAdvisor<T1..T12>` |
| `AdviceRunner\`14.cs` | `TAdvisor, T1..T13` | `IAdvisor<T1..T13>` |
| `AdviceRunner\`15.cs` | `TAdvisor, T1..T14` | `IAdvisor<T1..T14>` |
| `AdviceRunner\`16.cs` | `TAdvisor, T1..T15` | `IAdvisor<T1..T15>` |
| `AdviceRunner\`17.cs` | `TAdvisor, T1..T16` | `IAdvisor<T1..T16>` |

There is no `AdviceRunner\`1.cs` and no `IAdvisor` zero-argument variant. The minimum arity is 1.

## Execution algorithm

Every runner follows the same algorithm:

1. **Resolve** — call `ctx.ServiceProvider.GetServices<TAdvisor>()` to get all registered implementations.
2. **Sort** — order by `IAdvisor.Order` ascending using LINQ `OrderBy`. Advisors with the same `Order` run in DI registration order.
3. **Execute** — iterate the sorted list. Before each advisor, check `ct.ThrowIfCancellationRequested()`.
4. **Short-circuit** — if any advisor returns a result other than `AdviseResult.Continue`, return that result immediately without calling the remaining advisors.
5. **Default** — if all advisors return `Continue`, return `AdviseResult.Continue`.

## Generated extension methods

The source generator (`Schemata.Advice.Generator`) emits `RunAsync` extension methods on `AdvicePipeline<TAdvisor>` that delegate to the appropriate `AdviceRunner<...>`. The generated call site looks like:

```csharp
// Generated for IRepositoryAddAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
public static Task<AdviseResult> RunAsync<TEntity>(
    this AdvicePipeline<IRepositoryAddAdvisor<TEntity>> _,
    AdviceContext         ctx,
    IRepository<TEntity> a1,
    TEntity              a2,
    CancellationToken    ct = default)
    where TEntity : class
    => AdviceRunner<IRepositoryAddAdvisor<TEntity>, IRepository<TEntity>, TEntity>
        .RunAsync(ctx, a1, a2, ct);
```

The `_` parameter (the `AdvicePipeline<TAdvisor>` struct) is discarded. It exists only to make the extension method resolve on the correct type. See [Generator](generator.md) for how the generator produces these methods.

## AdvicePipeline\<TAdvisor\>

`AdvicePipeline<TAdvisor>` is a zero-size `readonly struct` in `Schemata.Advice`. It carries no fields and causes no heap allocation. Its constraint `where TAdvisor : IAdvisor` ensures only valid advisor interfaces can be used as the type argument.

```csharp
public readonly struct AdvicePipeline<TAdvisor>
    where TAdvisor : IAdvisor;
```

`Advisor.For<TAdvisor>()` returns `default` (the zero-value of the struct). The call is a no-op at runtime; the struct is used only as a compile-time dispatch token.

## Extension points

- To add a new advisor interface with a new arity, define `IMyAdvisor<T1, T2> : IAdvisor<T1, T2>`. The generator emits the `RunAsync` extension automatically. The corresponding `AdviceRunner\`3.cs` already exists in the runtime.
- To add a new advisor for an existing interface, implement the interface and register via `TryAddEnumerable`. No changes to the runner are needed.
- The maximum supported arity is 16. If you need more than 16 domain arguments, wrap them in a context object and use arity 1.

## Design motivation

Static classes with a single `RunAsync` method avoid virtual dispatch and interface overhead on the runner itself. The zero-size struct dispatch token avoids heap allocation on every pipeline invocation. Separating the runner from the pipeline token means the generator can emit extension methods without knowing about the runner implementation.

## Caveats

- Arity is **1..16**. There is no zero-argument `AdviceRunner` and no `IAdvisor` zero-argument variant. An advisor that needs no domain arguments should use arity 1 with a dummy or context-carrying argument.
- `GetServices<TAdvisor>()` is called on every `RunAsync` invocation. For hot paths, consider caching the sorted advisor list if the DI container supports it.
- Advisors are resolved from the `AdviceContext.ServiceProvider`, which is the request-scoped service provider. Singleton advisors are resolved from the root scope.

## See also

- [Advice Overview](overview.md) — `AdvicePipeline`, `Advisor`, short-circuit semantics
- [Advice Generator](generator.md) — how `RunAsync` extension methods are emitted
- [Advice Pipeline](../core/advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
