# Custom Expression Language

## What you'll build

A minimal `IExpressionCompiler` for a "Simple" filter language, registered under a keyed DI name and used
directly against a query. You'll also see why `ResourceOperationHandler.ListAsync` never calls it and what to do
instead.

## Prerequisites

- Familiarity with the AIP-160 filter language from [Filtering and Pagination](../guides/filtering-and-pagination.md).
- NuGet packages: `Schemata.Expressions.Skeleton`, `Schemata.Expressions.Aip`.

## Step 1: Implement the AST node

`IExpressionTree` requires a `Language` property. The AST root carries it plus the source string used for cache
keying:

```csharp
using Schemata.Expressions.Skeleton;

public static class SimpleLanguage
{
    public const string Name = "simple";
}

public sealed class SimpleTree : IExpressionTree
{
    public SimpleTree(string source) { Source = source; }
    public string Language => SimpleLanguage.Name;
    public string Source   { get; }
}
```

## Step 2: Implement the compiler

```csharp
using System;
using System.Linq.Expressions;
using Schemata.Expressions.Skeleton;

public sealed class SimpleCompiler : IExpressionCompiler
{
    public string Language => SimpleLanguage.Name;

    public IExpressionTree Parse(string source) {
        var key = ExpressionCacheKey.Create(Language, source, null, null, null);
        return ExpressionCache.GetOrAddTree(key, () => new SimpleTree(source));
    }

    public Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree tree, ExpressionCompileOptions? options = null) {
        if (tree is not SimpleTree node) {
            throw new ArgumentException("Tree must be a SimpleTree.", nameof(tree));
        }

        if (typeof(TResult) != typeof(bool)) {
            throw new NotSupportedException("SimpleCompiler only supports a bool result.");
        }

        var key = ExpressionCacheKey.Create(Language, node.Source, typeof(TContext), typeof(TResult), null);
        return ExpressionCache.GetOrAddExpression(key, () => {
            // A real compiler parses node.Source. This stub matches everything.
            var param = Expression.Parameter(typeof(TContext), "e");
            var body  = Expression.Constant(true);
            return (Expression<Func<TContext, TResult>>)(object)Expression.Lambda<Func<TContext, bool>>(body, param);
        });
    }
}
```

**Assertion:** `new SimpleCompiler().Language` is `"simple"`. `Parse("title = 'foo'")` returns a `SimpleTree`, and
`Compile<Student, bool>(tree)` returns a lambda that evaluates to `true`.

## Step 3: Register under a keyed DI name

Mirror `AddAipExpressions`:

```csharp
schema.ConfigureServices(services => {
    services.AddKeyedSingleton<IExpressionCompiler, SimpleCompiler>(SimpleLanguage.Name);
    // If the language has order-by:
    // services.AddKeyedSingleton<IOrderCompiler, SimpleOrderCompiler>(SimpleLanguage.Name);
});
```

**Assertion:** resolving `IExpressionCompiler` keyed by `SimpleLanguage.Name` yields a `SimpleCompiler`.

## Step 4: Use the compiler directly

```csharp
var compiler = sp.GetRequiredKeyedService<IExpressionCompiler>(SimpleLanguage.Name);
var tree     = compiler.Parse("title = 'Les Misérables'");
var filter   = compiler.Compile<Book, bool>(tree);

var results  = books.Where(filter).ToList();
// or, for a single object:
var matches  = ExpressionRuntime.Evaluate(filter, book);
```

**Assertion:** the compiled filter applies to an `IQueryable<Book>` and evaluates against a single `Book`.

## Step 5: The resource list endpoint stays on AIP

`ResourceOperationHandler.ListAsync` resolves the filter compiler with a fixed key:

```csharp
var compiler = _sp.GetRequiredKeyedService<IExpressionCompiler>(AipLanguage.Name);
```

Your compiler is never called by the list endpoint, whatever the client sends in `filter`. To use a custom
language for resource filtering:

1. **Translate in a request advisor.** Implement `IResourceListRequestAdvisor<TEntity>`, parse your syntax, and
   apply the predicate via `container.ApplyModification`.
2. **Build a custom endpoint** that resolves your compiler by key and applies it to the repository query,
   bypassing the handler.

Neither requires changing framework code.

**Assertion:** with `SimpleCompiler` registered, `GET /v1/books?filter=title='foo'` is still parsed by the AIP
compiler, not `SimpleCompiler`.

## Common pitfalls

- **Registering without a key.** `services.AddSingleton<IExpressionCompiler, SimpleCompiler>()` is not
  discoverable by `GetRequiredKeyedService`. Always use `AddKeyedSingleton` with the language name.
- **Forgetting `IExpressionTree.Language`.** The AST root must implement the `Language` property; an AST type
  without it does not satisfy the interface.
- **CEL has no `IOrderCompiler`.** Resolving `IOrderCompiler` keyed by `CelLanguage.Name` throws. Use AIP for
  ordering when CEL is the filter language.

## See also

- [Custom Expression Language](../documents/expressions/custom-language.md) — the full reference
- [Expressions Overview](../documents/expressions/overview.md)
- [AIP Expressions](../documents/expressions/aip.md)
- [Filtering](../documents/resource/filtering.md)
