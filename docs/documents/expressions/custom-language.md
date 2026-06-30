# Custom Expression Language

A custom filter language supplies an `IExpressionCompiler`, an `ExpressionLanguageDescriptor`, and a `Use*` extension over `IExpressionLanguageBuilder`. Add an `IExpressionPushdownPlanner` when the language can split filters for backend execution and local residual evaluation.

## Where the code lives

| Package                         | Key files                                                                                                                                |
| ------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Expressions.Skeleton` | `IExpressionCompiler.cs`, `IExpressionTree.cs`, `ExpressionCompileOptions.cs`, `ExpressionFunction.cs`                                   |
| `Schemata.Expressions.Skeleton` | `ExpressionLanguageProfile.cs`, `ExpressionLanguageDescriptor.cs`, `IExpressionLanguageBuilder.cs`, `FilteringMode.cs`                   |
| `Schemata.Expressions.Skeleton` | `IExpressionPushdownPlanner.cs`, `ExpressionPushdownPlan.cs`, `ExpressionCapabilities.cs`, `ExpressionCache.cs`, `ExpressionCacheKey.cs` |

## Implement the language identity

Use one constant for the DI key, descriptor, profile entry, compiler, and planner:

```csharp
public static class MyLanguage
{
    public const string Name = "my-lang";
}
```

## Implement `IExpressionTree`

The AST root implements `IExpressionTree` and carries the source used for cache keying:

```csharp
public sealed class MyTree : IExpressionTree
{
    public MyTree(string source) { Source = source; }

    public string Language => MyLanguage.Name;
    public string Source { get; }
}
```

## Implement `IExpressionCompiler`

Cache parsing and compilation separately:

```csharp
public sealed class MyCompiler : IExpressionCompiler
{
    public string Language => MyLanguage.Name;

    public IExpressionTree Parse(string source) {
        var key = ExpressionCacheKey.Create(Language, source, null, null, null);
        return ExpressionCache.GetOrAddTree(key, () => MyParser.Parse(source));
    }

    public Expression<Func<TContext, TResult>> Compile<TContext, TResult>(
        IExpressionTree tree,
        ExpressionCompileOptions? options = null) {
        if (tree is not MyTree node) {
            throw new ArgumentException("Tree must be a MyTree.", nameof(tree));
        }

        var fingerprint = ExpressionCompileOptions.Fingerprint(options);
        var key = ExpressionCacheKey.Create(
            Language,
            node.Source,
            typeof(TContext),
            typeof(TResult),
            fingerprint);

        return ExpressionCache.GetOrAddExpression(key, () => {
            var visitor = new MyCompileVisitor(typeof(TContext), options);
            var body = visitor.Visit(node);
            if (body.Type != typeof(TResult)) {
                body = Expression.Convert(body, typeof(TResult));
            }
            return Expression.Lambda<Func<TContext, TResult>>(body, visitor.Parameter);
        });
    }
}
```

Include option data in the fingerprint whenever `ExpressionCompileOptions.Functions` or language-specific options change generated output.

## Register services

Provide one service registration extension. Register the descriptor under the language name and set `SupportsValues` according to what the compiler can return:

```csharp
public static class MyServiceCollectionExtensions
{
    public static IServiceCollection AddMyExpressions(
        this IServiceCollection services,
        Action<ExpressionLanguageOptions>? configure = null) {
        var options = new ExpressionLanguageOptions();
        configure?.Invoke(options);

        services.AddKeyedSingleton<IExpressionCompiler, MyCompiler>(MyLanguage.Name);
        services.AddKeyedSingleton(
            MyLanguage.Name,
            new ExpressionLanguageDescriptor(
                MyLanguage.Name,
                options.Filtering,
                options.MaxResidualScanRows,
                SupportsValues: true));

        return services;
    }
}
```

Use `SupportsValues: false` for predicate-only languages. Use `SupportsValues: true` when the compiler supports scalar values for modules that evaluate conditions or computed expressions.

## Add the language-builder seam

Expose `Use<MyLang><T>` over `IExpressionLanguageBuilder` so Resource, Insight, Flow, or another module can enable the language in its profile:

```csharp
public static class MyExpressionLanguageBuilderExtensions
{
    public static T UseMyLang<T>(
        this T builder,
        Action<ExpressionLanguageEntry>? configure = null)
        where T : IExpressionLanguageBuilder {
        builder.Services.AddMyExpressions();
        var entry = builder.Languages.Enable(MyLanguage.Name);
        configure?.Invoke(entry);
        return builder;
    }
}
```

A host can then write:

```csharp
schema.UseResource()
      .UseMyLang(entry => {
          entry.Filtering = FilteringMode.Residual;
          entry.MaxResidualScanRows = 5_000;
      })
      .UseOrdering();
```

The first enabled language in the profile becomes the default when a request omits `language`. A request with an explicit language must name one enabled entry.

## Add pushdown when the backend can translate part of the language

Implement `IExpressionPushdownPlanner` when a backend can execute a safe subset:

```csharp
public sealed class MyPushdownPlanner : IExpressionPushdownPlanner
{
    public string Language => MyLanguage.Name;

    public ExpressionPushdownPlan Plan(IExpressionTree tree, ExpressionCapabilities capabilities) {
        if (tree is not MyTree node) {
            throw new ArgumentException("Tree must be a MyTree.", nameof(tree));
        }

        return MyPushdown.Split(node, capabilities);
    }
}
```

Register it under the same language key:

```csharp
services.AddKeyedSingleton<IExpressionPushdownPlanner, MyPushdownPlanner>(MyLanguage.Name);
```

Only push constructs whose backend translation preserves the language's null, error, comparison, and function semantics. Put uncertain constructs in the residual. See [Pushdown and Residual Evaluation](pushdown.md).

## Ordering

Order-by is language-independent. If the module exposes AIP-132 `order_by`, enable `Schemata.Expressions.Order`:

```csharp
schema.UseResource()
      .UseMyLang()
      .UseOrdering();
```

Custom order syntaxes should use a separate surface. The built-in resource list handler expects the non-keyed `IOrderCompiler` contract.

## Resource list integration

`ResourceOperationHandler.ListAsync` resolves a `ResolvedLanguage` through `ExpressionLanguageResolver` using `SchemataResourceOptions.Expressions`. That profile is populated by the resource builder when you call `UseMyLang`, `UseAip`, or `UseCel`.

The list handler then resolves `IExpressionCompiler` keyed by `ResolvedLanguage.Language`. In `FilteringMode.Residual`, it also resolves `IExpressionPushdownPlanner` keyed by the same language, applies the pushed tree to the repository query, and evaluates the residual locally through `ResidualPage`. In `Strict`, it compiles the whole tree.

Order-by uses the non-keyed `IOrderCompiler`, so enable `UseOrdering()` separately.

## Direct use

```csharp
var compiler = sp.GetRequiredKeyedService<IExpressionCompiler>(MyLanguage.Name);
var tree = compiler.Parse("grade > 3");
var filter = compiler.Compile<Student, bool>(tree);
var students = dbContext.Students.Where(filter).ToList();
```

`ExpressionRuntime.Evaluate(filter, student)` evaluates a compiled expression against one object.

## Caveats

- Keyed compiler, descriptor, planner, and profile entry names must match exactly.
- A singleton compiler or planner must be thread-safe; `Parse`, `Compile`, and `Plan` can run concurrently.
- A module profile with no enabled languages causes `ExpressionLanguageResolver.Resolve` to throw `UnknownExpressionLanguageException`.

## See also

- [Expressions Overview](overview.md)
- [Pushdown and Residual Evaluation](pushdown.md)
- [AIP Expressions](aip.md)
- [CEL Expressions](cel.md)
- [Custom Expression Language](../../cookbook/custom-expression-language.md)
- [Resource Filtering](../resource/filtering.md)
