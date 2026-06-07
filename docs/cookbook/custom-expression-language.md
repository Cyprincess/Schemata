# Custom Expression Language

## What you'll build

A minimal `IExpressionCompiler` implementation for a hypothetical "Simple" filter language, registered under a keyed DI name. You'll also understand why this compiler won't be used by `ResourceOperationHandler.ListAsync` automatically, and what you can do instead.

## Prerequisites

- Familiarity with the AIP-160 filter language from [Filtering and Pagination](../guides/filtering-and-pagination.md).
- NuGet packages: `Schemata.Expressions.Skeleton`, `Schemata.Expressions.Aip`.

## Step 1: Understand the contracts

`IExpressionCompiler` has two methods:

```csharp
public interface IExpressionCompiler
{
    string Language { get; }

    IExpressionTree Parse(string source);

    Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree           tree,
        ExpressionCompileOptions? options = null
    );
}
```

`Parse` turns a filter string into an `IExpressionTree` (an AST). `Compile` turns that tree into a typed LINQ expression. `Language` is the key used for DI lookup.

`IOrderCompiler` handles `order_by` clauses:

```csharp
public interface IOrderCompiler
{
    string Language { get; }

    Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
        string source,
        ExpressionCompileOptions? options = null
    );
}
```

The AIP implementation registers both under `AipLanguage.Name`. CEL registers only `IExpressionCompiler` — there is no CEL `IOrderCompiler`.

## Step 2: Implement a minimal compiler

```csharp
using System;
using System.Linq.Expressions;
using Schemata.Expressions.Skeleton;

public static class SimpleLanguage
{
    public const string Name = "simple";
}

// Minimal AST node
public sealed class SimpleTree : IExpressionTree
{
    public SimpleTree(string source) { Source = source; }
    public string Source { get; }
}

public sealed class SimpleCompiler : IExpressionCompiler
{
    public string Language => SimpleLanguage.Name;

    public IExpressionTree Parse(string source) => new SimpleTree(source);

    public Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree           tree,
        ExpressionCompileOptions? options = null
    ) {
        // Real implementation would parse tree.Source into a LINQ expression.
        // This stub always returns a "match all" predicate for illustration.
        if (typeof(TResult) != typeof(bool)) {
            throw new NotSupportedException($"SimpleCompiler only supports bool result type.");
        }

        var param = Expression.Parameter(typeof(TContext), "e");
        var body  = Expression.Constant(true);
        return (Expression<Func<TContext, TResult>>)(object)Expression.Lambda<Func<TContext, bool>>(body, param);
    }
}
```

**Assertion:** `new SimpleCompiler().Language` returns `"simple"`. `Parse("title = 'foo'")` returns a `SimpleTree`. `Compile<Student, bool>(tree)` returns a lambda that always evaluates to `true`.

## Step 3: Register under a keyed DI name

Mirror the pattern from `AddAipExpressions`:

```csharp
services.AddKeyedSingleton<IExpressionCompiler, SimpleCompiler>(SimpleLanguage.Name);
// Optionally register an order compiler too:
// services.AddKeyedSingleton<IOrderCompiler, SimpleOrderCompiler>(SimpleLanguage.Name);
```

In a Schemata startup context, write this into the `schema.Services` buffer:

```csharp
schema.Services.AddKeyedSingleton<IExpressionCompiler, SimpleCompiler>(SimpleLanguage.Name);
```

**Assertion:** Resolve `IExpressionCompiler` keyed by `SimpleLanguage.Name` from the DI container. The result is a `SimpleCompiler` instance.

## Step 4: Use the compiler directly

Resolve the compiler by key and call it:

```csharp
var compiler = sp.GetRequiredKeyedService<IExpressionCompiler>(SimpleLanguage.Name);
var tree     = compiler.Parse("title = 'Les Misérables'");
var filter   = compiler.Compile<Book, bool>(tree);

// Apply to a queryable:
var results = books.Where(filter).ToList();
```

`ExpressionRuntime.Evaluate` is available for single-object evaluation:

```csharp
var matches = ExpressionRuntime.Evaluate(filter, book);
```

**Assertion:** The filter expression compiles and can be applied to an `IQueryable<Book>` or evaluated against a single `Book` instance.

## Step 5: Understand the hard-wired AIP limitation

`ResourceOperationHandler.ListAsync` resolves the filter compiler with a hard-coded key:

```csharp
var compiler = _sp.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name);
```

This means **your custom compiler is never called by the resource list endpoint**, regardless of what the client sends in the `filter` query parameter. The AIP compiler is always used for resource filtering.

To use a custom language in a resource context, you have two options:

1. **Pre-process the filter string** in a request advisor before it reaches the handler, translating your language into AIP-160 syntax.
2. **Build a custom list endpoint** that resolves your compiler by key and applies it to the repository query directly, bypassing `ResourceOperationHandler`.

Neither option requires modifying framework code.

**Assertion:** Register `SimpleCompiler` and send `GET /books?filter=title%3D'foo'`. The response uses AIP-160 parsing, not `SimpleCompiler`. The AIP parser will likely reject the `=` operator (AIP-160 uses `=` for equality but with different quoting rules) and return a filter error.

## Common pitfalls

- **Registering without a key.** `services.AddSingleton<IExpressionCompiler, SimpleCompiler>()` (no key) does not make the compiler discoverable by `GetRequiredKeyedService`. Always use `AddKeyedSingleton` with your language name as the key.
- **Assuming `IOrderCompiler` is optional.** If your language supports `order_by` and a caller resolves `IOrderCompiler` by your key, a missing registration throws `InvalidOperationException`. Register a stub or a real implementation.
- **CEL has no `IOrderCompiler`.** The CEL compiler (`Schemata.Expressions.Cel`) registers only `IExpressionCompiler`. Resolving `IOrderCompiler` keyed by `CelLanguage.Name` throws. Use AIP for ordering when CEL is the filter language.
- **`ExpressionCache.GetOrAddDelegate` caches compiled delegates by expression identity.** If your `Compile` method returns a new lambda instance on every call for the same logical filter, the cache won't deduplicate. Implement `IExpressionTree` equality or cache the compiled expression yourself.

## See also

- [Expressions overview](../documents/expressions/overview.md)
- [AIP expression language](../documents/expressions/aip.md)
- [CEL expression language](../documents/expressions/cel.md)
- [Resource filtering](../documents/resource/filtering.md)
- [Filtering and Pagination guide](../guides/filtering-and-pagination.md)
