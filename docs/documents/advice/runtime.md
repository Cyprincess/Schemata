# Advice Runtime

The advice runtime is the set of static `AdviceRunner` classes that execute the advisor
chain-of-responsibility. Each class covers one arity of the `IAdvisor<T1, ..., TN>` family. The
runtime supports arities **1 through 16** — every advisor receives at least one domain argument
beyond `AdviceContext` and `CancellationToken`.

## Where the code lives

| Package                 | Key files                                                                       |
| ----------------------- | ------------------------------------------------------------------------------- |
| `Schemata.Advice`       | `AdvicePipeline.cs`, `Advisor.cs`                                               |
| `Schemata.Abstractions` | `Advisors/IAdvisor.cs`, `Advisors/AdviceContext.cs`, `Advisors/AdviseResult.cs` |

The `AdviceRunner` classes have no source files; `Schemata.Advice.Generator` emits them into the
`Schemata.Advice` compilation.

## AdviceRunner family

Each `AdviceRunner<TAdvisor, T1, ..., TN>` is a static class with a single `RunAsync` method, and
its class constraint pins `TAdvisor` to the matching `IAdvisor<T1, ..., TN>`.

### Arity 1

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
            if (result is not AdviseResult.Continue) return result;
        }

        return AdviseResult.Continue;
    }
}
```

Higher arities follow the same shape, adding `T2 a2`, `T3 a3`, up to `T16 a16`.

### Emitted by the source generator

`Schemata.Advice.Generator` emits one class per arity into the compilation that declares
`AdvicePipeline<TAdvisor>`, which only `Schemata.Advice` does:

- class name `AdviceRunner<TAdvisor, T1, ..., TN>`, constrained to `IAdvisor<T1, ..., TN>`;
- hint name `AdviceRunner.ArityN.g.cs`;
- CLR metadata arity `N + 1` (`TAdvisor` plus the argument type parameters).

The generator caps the family at the 16 `IAdvisor<T1, ..., TN>` overloads declared in
`Schemata.Abstractions`. A compilation that only references `AdvicePipeline<TAdvisor>` receives
the generated `RunAsync` extensions and no runner types; see `generator.md`.

The minimum arity is 1; there is no zero-argument runner or `IAdvisor` zero-argument variant.

## Execution algorithm

1. **Resolve** — `ctx.ServiceProvider.GetServices<TAdvisor>()`.
2. **Sort** — `OrderBy(a => a.Order)` ascending; ties run in DI registration order.
3. **Cancel** — `ct.ThrowIfCancellationRequested()` before each advisor.
4. **Short-circuit** — return the first non-`Continue` result.
5. **Default** — return `Continue` when every advisor continues.

## Generated extension methods

`Schemata.Advice.Generator` emits `RunAsync` extensions on `AdvicePipeline<TAdvisor>` that delegate
to the right runner:

```csharp
// Generated for IRepositoryAddAdvisor<TEntity> : IAdvisor<IRepository<TEntity>, TEntity>
public static Task<AdviseResult> RunAsync<TEntity>(
    this AdvicePipeline<IRepositoryAddAdvisor<TEntity>> _,
    AdviceContext        ctx,
    IRepository<TEntity> a1,
    TEntity              a2,
    CancellationToken    ct = default)
    where TEntity : class
    => AdviceRunner<IRepositoryAddAdvisor<TEntity>, IRepository<TEntity>, TEntity>
        .RunAsync(ctx, a1, a2, ct);
```

The `_` parameter (the `AdvicePipeline<TAdvisor>` token) is discarded; it exists only so the
extension resolves on the correct type.

## AdvicePipeline\<TAdvisor\>

`AdvicePipeline<TAdvisor>` is a zero-size `readonly struct` in `Schemata.Advice`:

```csharp
public readonly struct AdvicePipeline<TAdvisor>
    where TAdvisor : IAdvisor;
```

`Advisor.For<TAdvisor>()` returns `default`. The struct is a compile-time dispatch token only.

## Extension points

- A new advisor interface with an existing arity (e.g. `IMyAdvisor<T1, T2> : IAdvisor<T1, T2>`)
  needs no runtime change; the generator emits its `RunAsync` and the emitted runner family
  already covers that arity.
- A new advisor for an existing interface needs only an implementation plus a `TryAddEnumerable`
  registration.
- Beyond 16 domain arguments, wrap them in a context object and use arity 1.

## API reference coverage

`docs/docfx.json` builds metadata with `SchemataSkipGenerators=true`, which removes the generator
from the `src/` projects, so the DocFX API reference lists neither the generated `RunAsync`
extensions nor the emitted `AdviceRunner` classes. Both appear in package IntelliSense and
compile-time diagnostics.

## Design rationale

Static classes with a single `RunAsync` avoid virtual dispatch on the runner. The zero-size token
avoids heap allocation per invocation. Keeping the runner separate from the token lets the
generator emit extensions without knowing the runner's internals.

## Caveats

- `GetServices<TAdvisor>()` runs on every invocation; on hot paths consider caching the sorted list
  if your container allows it.
- Advisors resolve from `AdviceContext.ServiceProvider`, the request scope; singleton advisors
  resolve from the root scope.

## See also

- [Advice Overview](overview.md) — `AdvicePipeline`, `Advisor`, short-circuit semantics
- [Advice Generator](generator.md) — how `RunAsync` extensions are emitted
- [Advice Pipeline](../core/advice-pipeline.md) — `IAdvisor`, `AdviceContext`, `AdviseResult`
