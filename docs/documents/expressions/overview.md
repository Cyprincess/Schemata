# Expressions Overview

The expression stack compiles filter strings (AIP-160, CEL) into LINQ `Expression<Func<TContext, TResult>>` trees
that apply directly to `IQueryable<T>` queries, and order-by strings (AIP-132) into ordering functions. Three
packages form the stack: `Schemata.Expressions.Skeleton` holds the contracts and shared cache,
`Schemata.Expressions.Aip` implements the AIP language, and `Schemata.Expressions.Cel` implements CEL. Each
language registers as a keyed DI service.

## Where the code lives

| Package | Key files |
| --- | --- |
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
        IExpressionTree tree, ExpressionCompileOptions? options = null);
}
```

`Parse` produces an `IExpressionTree` (the AST); `Compile` turns it into a typed lambda. The two steps are cached
independently, so a tree parsed once can be compiled for several context types without re-parsing. `Language` is
the DI registration key.

### `IOrderCompiler`

```csharp
public interface IOrderCompiler
{
    string Language { get; }
    Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
        string source, ExpressionCompileOptions? options = null);
}
```

`CompileOrder` parses and compiles an order-by string into a function that applies an `OrderBy`/`ThenBy` chain.

### `IExpressionTree`

```csharp
public interface IExpressionTree
{
    string Language { get; }
}
```

The AST root type implements it. `AipParser`'s `Filter` and `CelNode` both carry a `Source` string used as the
cache key on `Compile`.

### `ExpressionCompileOptions`

```csharp
public sealed class ExpressionCompileOptions
{
    public IDictionary<string, ExpressionFunction> Functions { get; }
}
```

`Functions` injects named `ExpressionFunction` factories into a compiler. Both the AIP and CEL compilers consult
`options.Functions` and fold the function set into the cache key, so two option sets binding the same name to
different delegates do not share a compiled result.

## `ExpressionCache`

`ExpressionCache` is a process-wide static with three LRU caches:

| Cache | Capacity | Key | Value |
| --- | --- | --- | --- |
| `Trees` | 500 | `ExpressionCacheKey` | `IExpressionTree` |
| `Expressions` | 500 | `ExpressionCacheKey` | `LambdaExpression` |
| `Delegates` | 200 | `LambdaExpression` (reference identity) | `Delegate` |

### Cache key

`ExpressionCacheKey.Create` joins five inputs with the unit separator (``) and returns their SHA-256 hash:

```
language + source + contextType.AssemblyQualifiedName + resultType.AssemblyQualifiedName + options
```

The `options` component is a fingerprint of the custom functions. `Parse` keys with null context/result/options;
`Compile` keys with the full tuple.

### `LruCache<TKey, TValue>`

`LruCache` is a thread-safe doubly-linked-list plus dictionary with LRU eviction. `GetOrAdd` locks to check for a
hit, runs the factory outside the lock on a miss, then re-checks under lock before inserting, so concurrent
compilations of the same key produce one stored value.

### `ExpressionRuntime`

```csharp
public static TResult Evaluate<TContext, TResult>(
    Expression<Func<TContext, TResult>> expression, TContext context);
```

`Evaluate` resolves a compiled delegate from `ExpressionCache.GetOrAddDelegate` (keyed by lambda reference) and
invokes it against one object.

## Registration

```csharp
services.AddAipExpressions();  // IExpressionCompiler + IOrderCompiler keyed "aip"
services.AddCelExpressions();  // IExpressionCompiler keyed "cel"
```

`SchemataResourceFeature` calls `AddAipExpressions()` automatically. CEL is opt-in. Resolve a compiler by key:

```csharp
var aip = sp.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name); // "aip"
var cel = sp.GetRequiredKeyedService<IExpressionCompiler>(CelLanguage.Name); // "cel"
```

## Design rationale

Splitting `Parse` from `Compile` lets the tree cache serve many context types from one parse. The delegate
cache is keyed by lambda reference identity: the expression cache returns the same `LambdaExpression`
instance for identical inputs, so the delegate cache hit rate stays high.

## Caveats

- The three caches are process-wide statics with LRU eviction; they are never explicitly cleared.
- CEL implements `IExpressionCompiler` only, not `IOrderCompiler`. Resolving `IOrderCompiler` keyed by
  `CelLanguage.Name` throws.
- `ResourceOperationHandler.ListAsync` resolves both compilers by the fixed key `AipLanguage.Name`; a compiler
  registered under another key does not affect the resource system.

## See also

- [AIP Expressions](aip.md)
- [CEL Expressions](cel.md)
- [Custom Language](custom-language.md)
- [Filtering](../resource/filtering.md)
