# Custom Expression Language

You can register a custom filter or order-by compiler by implementing `IExpressionCompiler` and/or `IOrderCompiler` and registering them as keyed singletons in DI. The key is a string that identifies your language. Custom compilers integrate with `ExpressionCache` for caching and can be used anywhere the compiler interfaces are resolved by key.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Expressions.Skeleton` | `IExpressionCompiler.cs`, `IOrderCompiler.cs` |
| `Schemata.Expressions.Skeleton` | `ExpressionCache.cs`, `ExpressionCacheKey.cs` |
| `Schemata.Expressions.Skeleton` | `ExpressionCompileOptions.cs`, `ExpressionFunction.cs` |

## Implementing `IExpressionCompiler`

```csharp
public sealed class MyCompiler : IExpressionCompiler
{
    public string Language => "my-lang";

    public IExpressionTree Parse(string source) {
        // Parse source into an AST node that implements IExpressionTree.
        // Cache the result via ExpressionCache.GetOrAddTree.
        var key = ExpressionCacheKey.Create(Language, source, null, null, null);
        return ExpressionCache.GetOrAddTree(key, () => MyParser.Parse(source));
    }

    public Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree tree,
        ExpressionCompileOptions? options = null)
    {
        if (tree is not MyAstNode node) {
            throw new ArgumentException("Tree must be a MyAstNode.", nameof(tree));
        }

        var key = ExpressionCacheKey.Create(
            Language,
            node.ToString() ?? string.Empty,
            typeof(TContext),
            typeof(TResult),
            null);

        return ExpressionCache.GetOrAddExpression(key, () => {
            var visitor = new MyCompileVisitor(typeof(TContext));
            var body = visitor.Visit(node);
            if (body.Type != typeof(TResult)) {
                body = Expression.Convert(body, typeof(TResult));
            }
            return Expression.Lambda<Func<TContext, TResult>>(body, visitor.Parameter);
        });
    }
}
```

`IExpressionTree` is a marker interface with no members. Your AST root type just needs to implement it.

## Implementing `IOrderCompiler`

```csharp
public sealed class MyOrderCompiler : IOrderCompiler
{
    public string Language => "my-lang";

    public Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
        string source,
        ExpressionCompileOptions? options = null)
    {
        var fields = MyOrderParser.Parse(source);
        return query => {
            IOrderedQueryable<T>? ordered = null;
            foreach (var (field, descending) in fields) {
                var param = Expression.Parameter(typeof(T), "e");
                var member = Expression.Property(param, field);
                var lambda = Expression.Lambda(member, param);
                ordered = Apply(query, ordered, lambda, descending);
            }
            return ordered ?? query.OrderBy(_ => 0);
        };
    }

    // ... Apply helper using Queryable.OrderBy/ThenBy via reflection
}
```

## Registration

```csharp
services.AddKeyedSingleton<IExpressionCompiler, MyCompiler>("my-lang");
services.AddKeyedSingleton<IOrderCompiler, MyOrderCompiler>("my-lang");
```

Register both under the same key string. If your language has no order-by support, register only `IExpressionCompiler`.

## Using a custom compiler

Resolve by key and call `Parse`/`Compile` directly:

```csharp
var compiler = sp.GetRequiredKeyedService<IExpressionCompiler>("my-lang");
var tree = compiler.Parse("grade > 3");
var filter = compiler.Compile<Student, bool>(tree);

// Apply to a query
var students = dbContext.Students.Where(filter).ToList();
```

Or use `ExpressionRuntime.Evaluate` for single-object evaluation:

```csharp
var result = ExpressionRuntime.Evaluate(filter, student);
```

## Hard-wired-to-AIP caveat

`ResourceOperationHandler.ListAsync` resolves `IExpressionCompiler` and `IOrderCompiler` by the key `AipLanguage.Name` ("aip"). This key is hard-coded in the handler. Registering a custom compiler under a different key does not affect the resource system's filter or order behavior.

```csharp
// This does NOT affect ListAsync — it still uses "aip"
services.AddKeyedSingleton<IExpressionCompiler, MyCompiler>("my-lang");
```

To use a custom language for resource filtering, you would need to either:

1. Register your compiler under `AipLanguage.Name` ("aip"), replacing the built-in AIP compiler. This is not recommended unless your language is a strict superset of AIP-160.
2. Bypass `ResourceOperationHandler` and implement your own handler or advisor that resolves your compiler by key and applies the filter manually.

## `ExpressionCacheKey`

Use `ExpressionCacheKey.Create` to build cache keys for your compiler:

```csharp
var key = ExpressionCacheKey.Create(
    language: "my-lang",
    source: filterString,
    contextType: typeof(TContext),
    resultType: typeof(TResult),
    options: null   // or a fingerprint string for custom options
);
```

The key is a SHA-256 hash of the five inputs. Two compilations with the same inputs produce the same key and share the cached result.

## `ExpressionCompileOptions` and custom functions

`ExpressionCompileOptions.Functions` is a dictionary of named `ExpressionFunction` delegates. Your compiler can check this dictionary to support user-injected functions:

```csharp
public sealed class ExpressionFunction
{
    public ExpressionFunction(Func<IReadOnlyList<Expression>, Expression> builder) { ... }
}
```

The AIP compiler uses this mechanism for `timestamp` and `duration` overrides. If your compiler supports custom functions, include a fingerprint of the options in the cache key so different function sets produce different cache entries.

## Extension points

- Implement `IExpressionTree` on your AST root type. No members are required.
- Use `ExpressionCache.GetOrAddTree` and `ExpressionCache.GetOrAddExpression` to participate in the shared cache.
- Use `ExpressionCache.GetOrAddDelegate` to cache compiled delegates for `ExpressionRuntime.Evaluate`.

## Design motivation

The keyed DI pattern lets multiple languages coexist in the same application without conflict. Each language is a self-contained singleton that owns its parser and compiler. The shared `ExpressionCache` means all languages benefit from the same LRU caching infrastructure without duplicating it.

## Caveats

- Registering a custom compiler under `AipLanguage.Name` replaces the built-in AIP compiler for all consumers, including `ResourceOperationHandler.ListAsync`. Do this only if you intend to replace AIP-160 filtering globally.
- The `ExpressionCache` is process-wide and shared across all languages. Cache keys include the language name, so there is no collision between languages with the same source string.
- `IExpressionTree` is a marker interface. Your AST type must implement it, but no methods are required. The compiler is responsible for casting the tree to its own AST type in `Compile`.
- Custom compilers registered as singletons must be thread-safe. `Parse` and `Compile` may be called concurrently from multiple request threads.

## See also

- [Expressions Overview](overview.md)
- [AIP Expressions](aip.md)
- [CEL Expressions](cel.md)
- [Filtering](../resource/filtering.md)
