# Custom Expression Language

A custom filter language supplies an `IExpressionCompiler`, an `ExpressionLanguageDescriptor`, and a `Use*` extension over `IExpressionLanguageBuilder`. Add an `IExpressionPushdownPlanner` when the language can split filters for backend execution and local residual evaluation. Add an `IExpressionReferenceProvider` when the language is used as an Insight filter or selection expression so `PublicPlanValidator` can walk the expression's references and result shape against the source's public shape.

## Where the code lives

| Package                         | Key files                                                                                                                                |
| ------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `Schemata.Expressions.Skeleton` | `IExpressionCompiler.cs`, `IExpressionTree.cs`, `ExpressionCompileOptions.cs`, `ExpressionFunction.cs`, `IExpressionReferenceProvider.cs` |
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
        services.AddKeyedSingleton<IExpressionReferenceProvider, MyReferenceProvider>(MyLanguage.Name);
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

Use `SupportsValues: false` for predicate-only languages. Use `SupportsValues: true` when the compiler supports scalar values for modules that evaluate conditions or computed expressions. Register the reference provider when the language is used as an Insight filter or selection expression; a missing provider makes `PublicPlanValidator` reject the language with `Language '<name>' does not provide query field references.`

## Provide a reference provider

`IExpressionReferenceProvider.Analyze` walks the parsed tree and returns the references the expression
reads plus the structural provenance of its result:

```csharp
public interface IExpressionReferenceProvider
{
    ExpressionReferences Analyze(IExpressionTree tree);
}

public sealed record ExpressionReferences(IReadOnlyList<ExpressionReference> References, ExpressionShape Result);
```

`References` contains source-rooted `ExpressionReference` paths. String segments name members;
constant index segments retain their original value type, and `ExpressionPathMarker` distinguishes
element/key iteration from user keys. Literal ambiguity, repeated-field traversal and type-literal
metadata preserve language resolution rules. `Result` carries scalar, null, reference, sequence,
literal-map, map-value, concatenation or alternative provenance so computed aliases retain their
field boundary. Analyze before compiler optimizations can erase accesses such as `has(secret)`.

A predicate-only language can return `new ExpressionReferences(references, new ExpressionShape.Scalar())`
when every reference is a direct field read. Languages whose result may be a list, map, or null
return the matching `ExpressionShape` cases so the validator can bind them correctly:

```csharp
public sealed class MyReferenceProvider : IExpressionReferenceProvider
{
    public ExpressionReferences Analyze(IExpressionTree tree) {
        if (tree is not MyTree node) {
            throw new ArgumentException("Tree must be a MyTree.", nameof(tree));
        }

        var references = new List<ExpressionReference>();
        var result     = MyAnalyzer.Walk(node, references);
        return new ExpressionReferences(references, result);
    }
}
```

Register the reference provider under the same language key as the compiler and pushdown planner.
A language registered only with a compiler cannot validate against an Insight public shape.

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

To evaluate a compiled expression against one object, invoke it as a delegate. `ExpressionCache.GetOrAddDelegate(filter)(student)` compiles once and reuses the cached delegate on later calls.

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
