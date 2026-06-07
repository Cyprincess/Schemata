# Expressions Overview

The expression compiler stack translates filter strings (AIP-160) and order-by strings (AIP-132) into LINQ `Expression<Func<TContext, TResult>>` trees that can be applied directly to `IQueryable<T>` queries. Three packages form the stack: `Schemata.Expressions.Skeleton` defines the contracts and shared cache; `Schemata.Expressions.Aip` implements the AIP language; `Schemata.Expressions.Cel` implements the CEL language. Additional languages can be registered via keyed DI.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Expressions.Skeleton` | `IExpressionCompiler.cs`, `IOrderCompiler.cs`, `IExpressionTree.cs` |
| `Schemata.Expressions.Skeleton` | `ExpressionCache.cs`, `ExpressionCacheKey.cs`, `LruCache.cs` |
| `Schemata.Expressions.Skeleton` | `ExpressionRuntime.cs`, `ExpressionCompileOptions.cs`, `ExpressionFunction.cs` |
| `Schemata.Expressions.Aip` | `AipCompiler.cs`, `AipOrderCompiler.cs`, `ServiceCollectionExtensions.cs` |
| `Schemata.Expressions.Cel` | `CelCompiler.cs`, `ServiceCollectionExtensions.cs` |

## Contracts

### `IExpressionCompiler`

```csharp
public interface IExpressionCompiler
{
    string Language { get; }
    IExpressionTree Parse(string source);
    Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree tree,
        ExpressionCompileOptions? options = null);
}
```

`Parse` produces an `IExpressionTree` (the AST). `Compile` converts the tree to a typed lambda. Both steps are cached independently so a tree parsed once can be compiled for multiple context types without re-parsing.

### `IOrderCompiler`

```csharp
public interface IOrderCompiler
{
    string Language { get; }
    Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
        string source,
        ExpressionCompileOptions? options = null);
}
```

`CompileOrder` parses and compiles an order-by string directly to a `Func` that applies `OrderBy`/`ThenBy` chains to an `IQueryable<T>`.

### `ExpressionCompileOptions`

```csharp
public sealed class ExpressionCompileOptions
{
    public IDictionary<string, ExpressionFunction> Functions { get; }
}
```

Allows injecting custom named functions into the compiler. The AIP compiler checks `options.Functions` before falling back to built-ins (`timestamp`, `duration`). CEL does not currently support custom functions via this mechanism.

## `ExpressionCache`

`ExpressionCache` is a process-wide static class with three LRU caches:

| Cache | Capacity | Key type | Value type |
|---|---|---|---|
| `Trees` | 500 | `ExpressionCacheKey` | `IExpressionTree` |
| `Expressions` | 500 | `ExpressionCacheKey` | `LambdaExpression` |
| `Delegates` | 200 | `LambdaExpression` (by reference) | `Delegate` |

All three caches are process-wide singletons. They are not scoped to a request or a DI container.

### Cache key

`ExpressionCacheKey` is a SHA-256 hash of five inputs joined by the unit separator (`\u001f`):

```
language + source + contextType.AssemblyQualifiedName + resultType.AssemblyQualifiedName + options
```

The `options` component is produced by `AipBuiltInFunctions.Fingerprint(options)`, which encodes the names and identity hash codes of any custom functions. Two compilations with the same source but different custom functions produce different keys.

### `LruCache<TKey, TValue>`

`LruCache` is a thread-safe doubly-linked-list + dictionary LRU eviction cache. It uses a double-checked lock pattern: the first lock checks for a hit; if missed, the factory runs outside the lock; the second lock re-checks before inserting to handle concurrent compilation of the same key. The winner's value is stored; the loser's value is discarded.

### `ExpressionRuntime`

```csharp
public static TResult Evaluate<TContext, TResult>(
    Expression<Func<TContext, TResult>> expression,
    TContext context)
```

`ExpressionRuntime.Evaluate` compiles the lambda to a delegate (via `ExpressionCache.GetOrAddDelegate`) and invokes it. Use this when you need to evaluate a compiled expression against a single object rather than an `IQueryable`.

## Registration

### AIP

```csharp
services.AddAipExpressions();
// Registers:
//   IExpressionCompiler keyed "aip" -> AipCompiler (singleton)
//   IOrderCompiler keyed "aip"      -> AipOrderCompiler (singleton)
```

`SchemataResourceFeature` calls `services.AddAipExpressions()` automatically. You don't need to call it manually unless you're using the AIP compiler outside the resource system.

### CEL

```csharp
services.AddCelExpressions();
// Registers:
//   IExpressionCompiler keyed "cel" -> CelCompiler (singleton)
```

CEL has no `IOrderCompiler`. See [CEL](cel.md) for details.

## Resolving compilers

Compilers are registered as keyed singletons. Resolve them via:

```csharp
var compiler = sp.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name);
var order    = sp.GetRequiredKeyedService<IOrderCompiler>(AipLanguage.Name);
```

`AipLanguage.Name` is the string constant `"aip"`. `CelLanguage.Name` is `"cel"`.

## Design motivation

Separating `Parse` from `Compile` allows the tree cache to be shared across compilations for different context types. A filter string like `"grade > 3"` is parsed once and cached as an AST; it can then be compiled for `Student`, `Teacher`, or any other entity type without re-parsing.

The delegate cache (capacity 200) is keyed by lambda reference identity, not by content. This is intentional: two structurally identical lambdas compiled from the same source will share the same `LambdaExpression` instance (because the expression cache returns the same object), so the delegate cache hit rate is high.

## Caveats

- All three caches are process-wide statics. They are never cleared. In long-running processes with many distinct filter strings, the caches will fill and evict older entries. The LRU eviction policy ensures the most recently used entries are retained.
- `ExpressionCache` is not thread-safe for the factory call itself — two threads can race to compile the same key. The winner's result is stored; the loser's result is discarded. This is safe but means the factory may be called more than once for the same key under high concurrency.
- CEL does not implement `IOrderCompiler`. Calling `GetRequiredKeyedService<IOrderCompiler>(CelLanguage.Name)` will throw `InvalidOperationException`.
- `ResourceOperationHandler.ListAsync` is hard-wired to `AipLanguage.Name`. Registering a custom compiler under a different key does not affect the resource system's filter/order behavior.

## See also

- [AIP Expressions](aip.md)
- [CEL Expressions](cel.md)
- [Custom Language](custom-language.md)
- [Filtering](../resource/filtering.md)
