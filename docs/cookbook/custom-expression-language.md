# Custom Expression Language

## What you'll build

A minimal `IExpressionCompiler` for a "Simple" filter language, registered through the expression language builder seam and enabled on a module. The compiler matches every row, so the focus stays on language registration, profile selection, and resource list integration.

## Prerequisites

- Familiarity with [Expressions Overview](../documents/expressions/overview.md).
- NuGet packages: `Schemata.Expressions.Skeleton` and the module package you want to configure.

## Step 1: Implement the AST node

`IExpressionTree` requires a `Language` property. The AST root also carries the source string used for cache keying:

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
    public string Source { get; }
}
```

**Assertion:** `new SimpleTree("*").Language` is `"simple"`.

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
        IExpressionTree tree,
        ExpressionCompileOptions? options = null) {
        if (tree is not SimpleTree node) {
            throw new ArgumentException("Tree must be a SimpleTree.", nameof(tree));
        }

        if (typeof(TResult) != typeof(bool)) {
            throw new NotSupportedException("SimpleCompiler only supports a bool result.");
        }

        var key = ExpressionCacheKey.Create(
            Language,
            node.Source,
            typeof(TContext),
            typeof(TResult),
            ExpressionCompileOptions.Fingerprint(options));

        return ExpressionCache.GetOrAddExpression(key, () => {
            var param = Expression.Parameter(typeof(TContext), "e");
            var body = Expression.Constant(true);
            var lambda = Expression.Lambda<Func<TContext, bool>>(body, param);
            return (Expression<Func<TContext, TResult>>)(object)lambda;
        });
    }
}
```

**Assertion:** `Parse("title = 'foo'")` returns a `SimpleTree`, and `Compile<Book, bool>(tree)` returns a lambda that evaluates to `true`.

## Step 3: Register the language services

Create a service registration extension that mirrors the built-in language packages:

```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Expressions.Skeleton;

public static class SimpleServiceCollectionExtensions
{
    public static IServiceCollection AddSimpleExpressions(
        this IServiceCollection services,
        Action<ExpressionLanguageOptions>? configure = null) {
        var options = new ExpressionLanguageOptions();
        configure?.Invoke(options);

        services.AddKeyedSingleton<IExpressionCompiler, SimpleCompiler>(SimpleLanguage.Name);
        services.AddKeyedSingleton(
            SimpleLanguage.Name,
            new ExpressionLanguageDescriptor(
                SimpleLanguage.Name,
                options.Filtering,
                options.MaxResidualScanRows,
                SupportsValues: false));

        return services;
    }
}
```

**Assertion:** resolving `IExpressionCompiler` keyed by `SimpleLanguage.Name` yields a `SimpleCompiler`, and resolving `ExpressionLanguageDescriptor` with the same key yields `SupportsValues == false`.

## Step 4: Add the builder seam

Expose a `UseSimple` extension over `IExpressionLanguageBuilder`:

```csharp
using System;
using Schemata.Expressions.Skeleton;

public static class SimpleLanguageBuilderExtensions
{
    public static T UseSimple<T>(
        this T builder,
        Action<ExpressionLanguageEntry>? configure = null)
        where T : IExpressionLanguageBuilder {
        builder.Services.AddSimpleExpressions();
        var entry = builder.Languages.Enable(SimpleLanguage.Name);
        configure?.Invoke(entry);
        return builder;
    }
}
```

**Assertion:** calling `UseSimple(entry => entry.Filtering = FilteringMode.Residual)` adds one profile entry named `"simple"` and stores the entry override.

## Step 5: Enable it on a module builder

Use the host module's builder rather than raw `services.AddKeyedSingleton` calls:

```csharp
builder.UseSchemata(schema => {
    schema.UseResource()
          .UseSimple(entry => {
              entry.Filtering = FilteringMode.Strict;
          })
          .UseOrdering();
});
```

`UseSimple` writes the language into the module profile. `UseOrdering` registers the language-independent AIP-132 order compiler for `order_by`.

**Assertion:** a resource list request without `language` uses `SimpleLanguage.Name` when `UseSimple` is the first enabled language in the resource profile.

## Step 6: Use the compiler directly

Direct use still works when you do not need a module profile:

```csharp
var compiler = sp.GetRequiredKeyedService<IExpressionCompiler>(SimpleLanguage.Name);
var tree = compiler.Parse("title = 'Les Misérables'");
var filter = compiler.Compile<Book, bool>(tree);

var results = books.Where(filter).ToList();
var matches = ExpressionCache.GetOrAddDelegate(filter)(book);
```

**Assertion:** the compiled filter applies to an `IQueryable<Book>` and evaluates against a single `Book`.

## Resource list integration

`ResourceOperationHandler.ListAsync` resolves a language through `ExpressionLanguageResolver` against `SchemataResourceOptions.Expressions`. A resource profile is populated when the resource builder calls `UseSimple`, `UseAip`, or `UseCel`.

In `Strict`, the handler compiles the whole parsed tree and applies it to the repository query. In `Residual`, the handler also resolves an `IExpressionPushdownPlanner` keyed by the selected language, applies the pushed tree to the query, and runs the residual through `ResidualPage`.

The Simple language above does not register a pushdown planner, so configure it as `FilteringMode.Strict` for resource filtering or add a planner before enabling residual mode.

**Assertion:** with `UseSimple()` enabled on the resource builder, `GET /v1/books?language=simple&filter=anything` resolves `SimpleCompiler` instead of AIP.

## Common pitfalls

- **Registering without a key.** `services.AddSingleton<IExpressionCompiler, SimpleCompiler>()` is not discoverable by `GetRequiredKeyedService`. Use `AddKeyedSingleton` with the language name.
- **Skipping the descriptor.** `ExpressionLanguageResolver` reads `ExpressionLanguageDescriptor` for global defaults and `SupportsValues`.
- **Skipping the builder seam.** Raw service registration does not add the language to a module profile.
- **Enabling residual without a planner.** A residual resource filter resolves `IExpressionPushdownPlanner` keyed by the same language.
- **Expecting CEL order-by.** CEL has no `IOrderCompiler`; use `Schemata.Expressions.Order` through `UseOrdering()`.

## See also

- [Custom Expression Language](../documents/expressions/custom-language.md) — the full reference
- [Expressions Overview](../documents/expressions/overview.md)
- [Pushdown and Residual Evaluation](../documents/expressions/pushdown.md)
- [Resource Filtering](../documents/resource/filtering.md)
