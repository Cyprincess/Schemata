# Custom Expression Language

A custom filter or order-by language is an `IExpressionCompiler` (and optionally `IOrderCompiler`) registered as a
keyed singleton. The key is the language string. A custom compiler reuses `ExpressionCache` and is resolved
wherever the compiler interfaces are looked up by key.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Expressions.Skeleton` | `IExpressionCompiler.cs`, `IOrderCompiler.cs`, `IExpressionTree.cs` |
| `Schemata.Expressions.Skeleton` | `ExpressionCache.cs`, `ExpressionCacheKey.cs` |
| `Schemata.Expressions.Skeleton` | `ExpressionCompileOptions.cs`, `ExpressionFunction.cs` |

## Implementing `IExpressionCompiler`

The AST root implements `IExpressionTree`, which requires a `Language` property. Cache the parse with
`ExpressionCache.GetOrAddTree` and the compile with `ExpressionCache.GetOrAddExpression`:

```csharp
public sealed class MyTree : IExpressionTree
{
    public MyTree(string source) { Source = source; }
    public string Language => "my-lang";
    public string Source   { get; }
}

public sealed class MyCompiler : IExpressionCompiler
{
    public string Language => "my-lang";

    public IExpressionTree Parse(string source) {
        var key = ExpressionCacheKey.Create(Language, source, null, null, null);
        return ExpressionCache.GetOrAddTree(key, () => MyParser.Parse(source));
    }

    public Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree tree, ExpressionCompileOptions? options = null) {
        if (tree is not MyTree node) {
            throw new ArgumentException("Tree must be a MyTree.", nameof(tree));
        }

        var key = ExpressionCacheKey.Create(Language, node.Source, typeof(TContext), typeof(TResult), null);
        return ExpressionCache.GetOrAddExpression(key, () => {
            var visitor = new MyCompileVisitor(typeof(TContext));
            var body    = visitor.Visit(node);
            if (body.Type != typeof(TResult)) {
                body = Expression.Convert(body, typeof(TResult));
            }
            return Expression.Lambda<Func<TContext, TResult>>(body, visitor.Parameter);
        });
    }
}
```

## Implementing `IOrderCompiler`

```csharp
public sealed class MyOrderCompiler : IOrderCompiler
{
    public string Language => "my-lang";

    public Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
        string source, ExpressionCompileOptions? options = null) {
        var fields = MyOrderParser.Parse(source);
        return query => {
            IOrderedQueryable<T>? ordered = null;
            foreach (var (field, descending) in fields) {
                var param  = Expression.Parameter(typeof(T), "e");
                var member = Expression.Property(param, field);
                var lambda = Expression.Lambda(member, param);
                ordered = Apply(query, ordered, lambda, descending);
            }
            return ordered ?? query.OrderBy(_ => 0);
        };
    }
}
```

`AipOrderCompiler.CompileOrder` is a working reference for the `Apply` helper that chains
`OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending` via reflection on `Queryable`.

## Registration

```csharp
services.AddKeyedSingleton<IExpressionCompiler, MyCompiler>("my-lang");
services.AddKeyedSingleton<IOrderCompiler, MyOrderCompiler>("my-lang"); // only if the language has order-by
```

Register both under the same key. `AddAipExpressions` and `AddCelExpressions` follow this pattern.

## Using a custom compiler

```csharp
var compiler = sp.GetRequiredKeyedService<IExpressionCompiler>("my-lang");
var tree     = compiler.Parse("grade > 3");
var filter   = compiler.Compile<Student, bool>(tree);
var students = dbContext.Students.Where(filter).ToList();
```

`ExpressionRuntime.Evaluate(filter, student)` evaluates the compiled expression against a single object.

## The resource handler is fixed to AIP

`ResourceOperationHandler.ListAsync` resolves `IExpressionCompiler` and `IOrderCompiler` by the fixed key
`AipLanguage.Name`. A compiler registered under another key is never called by the list endpoint. To use a custom
language for resource filtering, either register it under `AipLanguage.Name` (replacing the AIP compiler
globally), or apply it in a custom advisor or endpoint that bypasses the handler. Neither requires changing
framework code.

## `ExpressionCacheKey` and custom options

`ExpressionCacheKey.Create(language, source, contextType, resultType, options)` returns a SHA-256 hash of its five
inputs. When a compiler supports custom functions, fold a fingerprint of `ExpressionCompileOptions.Functions` into
the `options` argument so different function sets produce different cache entries (`AipBuiltInFunctions.Fingerprint`
is a reference).

`ExpressionFunction` wraps a factory from argument expressions to a result expression:

```csharp
public sealed class ExpressionFunction
{
    public ExpressionFunction(Func<IReadOnlyList<Expression>, Expression> factory);
    public Func<IReadOnlyList<Expression>, Expression> Factory { get; }
    public Expression Build(IReadOnlyList<Expression> args);
}
```

## Caveats

- Registering a compiler under `AipLanguage.Name` replaces the built-in AIP compiler for every consumer,
  including `ListAsync`.
- The cache key includes the language name, so two languages with the same source string do not collide.
- A custom compiler registered as a singleton must be thread-safe; `Parse` and `Compile` run concurrently.

## See also

- [Expressions Overview](overview.md)
- [AIP Expressions](aip.md)
- [CEL Expressions](cel.md)
- [Custom Expression Language](../../cookbook/custom-expression-language.md)
