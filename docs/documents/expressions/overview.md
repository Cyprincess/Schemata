# Expressions Overview

The expression stack turns user filter text into LINQ expression trees, keeps language choice per module, and can split a filter between backend pushdown and local residual evaluation. Ordering is a separate AIP-132 compiler, so filter languages and order-by share contracts but register independently.

## Package split

| Package | Role |
| --- | --- |
| `Schemata.Expressions.Skeleton` | Contracts, cache, language profile/resolver, pushdown contracts, residual paging, and alias-keyed dynamic values. |
| `Schemata.Expressions.Aip` | AIP-160 filter parser/compiler plus AIP pushdown planner. |
| `Schemata.Expressions.Cel` | CEL parser/compiler plus pushdown planner and CEL value semantics. |
| `Schemata.Expressions.Order` | AIP-132 order-by compiler, independent of the filter language. |

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Expressions.Skeleton` | `IExpressionCompiler.cs`, `IOrderCompiler.cs`, `IExpressionTree.cs` |
| `Schemata.Expressions.Skeleton` | `ExpressionLanguageProfile.cs`, `ExpressionLanguageResolver.cs`, `ExpressionLanguageDescriptor.cs`, `ExpressionLanguageOptions.cs`, `ResolvedLanguage.cs` |
| `Schemata.Expressions.Skeleton` | `FilteringMode.cs`, `ExpressionCapabilities.cs`, `IExpressionPushdownPlanner.cs`, `ExpressionPushdownPlan.cs`, `ResidualPage.cs` |
| `Schemata.Expressions.Skeleton` | `DynamicValues.cs`, `ExpressionCache.cs`, `ExpressionCacheKey.cs`, `ExpressionCompileOptions.cs`, `ExpressionFunction.cs`, `ExpressionRuntime.cs` |
| `Schemata.Expressions.Aip` | `AipLanguage.cs`, `AipParser.cs`, `AipCompiler.cs`, `AipCompileVisitor.cs`, `AipPushdownPlanner.cs`, `ExpressionLanguageBuilderExtensions.cs`, `ServiceCollectionExtensions.cs` |
| `Schemata.Expressions.Cel` | `CelLanguage.cs`, `CelParser.cs`, `CelCompiler.cs`, `CelCompileVisitor.cs`, `CelValues.cs`, `CelDuration.cs`, `CelTimestamp.cs`, `CelError.cs`, `CelType.cs`, `CelPushdownPlanner.cs`, `Expressions/*.cs`, `ExpressionLanguageBuilderExtensions.cs`, `ServiceCollectionExtensions.cs` |
| `Schemata.Expressions.Order` | `OrderCompiler.cs`, `ServiceCollectionExtensions.cs` |

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

`Parse` returns a language AST. `Compile` turns that AST into a typed `Expression<Func<TContext, TResult>>`. `Language` is the keyed DI name used by `GetRequiredKeyedService<IExpressionCompiler>(name)`.

### `IOrderCompiler`

```csharp
public interface IOrderCompiler
{
    Func<IQueryable<T>, IOrderedQueryable<T>> CompileOrder<T>(
        string source,
        ExpressionCompileOptions? options = null);

    IReadOnlyList<OrderKey> Parse(string source);
}
```

`IOrderCompiler` is non-keyed. `Schemata.Expressions.Order.OrderCompiler` parses AIP-132 order-by text into `OrderKey` values and compiles them into an `OrderBy`/`ThenBy` chain.

### `IExpressionPushdownPlanner`

```csharp
public interface IExpressionPushdownPlanner
{
    string Language { get; }
    ExpressionPushdownPlan Plan(IExpressionTree tree, ExpressionCapabilities capabilities);
}
```

A planner receives a parsed tree and backend capabilities, then returns `ExpressionPushdownPlan(Pushed, Residual)`. The pushed part is a weakening of the original filter; `Pushed && Residual` is equivalent to the original. See [Pushdown and Residual Evaluation](pushdown.md).

## Language profiles

A module that accepts expressions owns an `ExpressionLanguageProfile`. The profile has an ordered `Languages` list, a module-level `Filtering` override, and a module-level `MaxResidualScanRows` override. Each `ExpressionLanguageEntry` names one language and can override `Filtering` and `MaxResidualScanRows` for that language in the module.

`IExpressionLanguageBuilder` is the shared builder seam:

```csharp
public interface IExpressionLanguageBuilder
{
    IServiceCollection Services { get; }
    ExpressionLanguageProfile Languages { get; }
}
```

Language packages expose `Use*` extensions over this seam. A resource module can enable AIP filtering, CEL filtering, and AIP-132 ordering without reaching for raw service registration:

```csharp
schema.UseResource()
      .UseAip(entry => entry.Filtering = FilteringMode.Residual)
      .UseCel(entry => entry.MaxResidualScanRows = 25_000)
      .UseOrdering();
```

`UseAip<T>` calls `AddAipExpressions()`, then enables `ExpressionLanguages.Aip` in the module profile. `UseCel<T>` does the same for `ExpressionLanguages.Cel`. `UseOrdering<T>` registers the non-keyed order compiler and does not add a filter language.

## Language resolver

`ExpressionLanguageResolver.Resolve(profile, requested, descriptors)` selects the language for a request:

1. An empty `requested` value picks the first enabled `ExpressionLanguageEntry`.
2. A non-empty request must match an enabled entry by ordinal string comparison.
3. A missing language or an empty profile throws `UnknownExpressionLanguageException`.
4. The returned `ResolvedLanguage` carries `Language`, effective `Filtering`, and effective `MaxResidualScanRows`.

`FilteringMode` has three values:

| Mode | Meaning |
| --- | --- |
| `Default` | Inherit from the other levels. If every level is `Default`, `OrStrict()` resolves to `Strict`. |
| `Strict` | Compile the whole filter for the backend path; untranslatable filters fail instead of running locally. |
| `Residual` | Push the translatable part and evaluate the rest locally under a scan cap. |

`Narrow` intersects modes. `Strict` at any level wins; `Residual` wins only when neither side is `Strict`; `Default` yields to the other side. The resolver applies descriptor defaults, then profile overrides, then entry overrides, and finally calls `OrStrict()`.

The residual scan cap has the same three levels. `ExpressionLanguageEntry.MaxResidualScanRows` wins when positive, then `ExpressionLanguageProfile.MaxResidualScanRows`, then `ExpressionLanguageDescriptor.MaxResidualScanRows`, then the built-in default of `10_000` rows.

## Language descriptors

`ExpressionLanguageDescriptor` records global defaults registered under the language name:

```csharp
public sealed record ExpressionLanguageDescriptor(
    string Language,
    FilteringMode Filtering,
    int MaxResidualScanRows,
    bool SupportsValues = false);
```

AIP registers `SupportsValues: false` because it is predicate-only. CEL registers `SupportsValues: true`, so modules that need scalar evaluation can select CEL for Insight planning and Flow conditions.

## Capabilities

`ExpressionCapabilities` tells a pushdown planner what a backend can translate:

| Flag | Meaning |
| --- | --- |
| `Comparison` | Equality and ordering comparisons. |
| `Logical` | Boolean composition: and, or, and not. |
| `Presence` | Field presence checks. |
| `Wildcard` | Wildcard text matching. |
| `Arithmetic` | Numeric arithmetic. |
| `Membership` | Collection membership checks. |
| `StringMatch` | String helper functions. |
| `Functions` | Extra named functions the backend can translate. |

`ExpressionCapabilities.Relational` is the default preset. Its flags are all true and `Functions` is empty, covering constructs relational repositories normally translate while leaving language-specific functions in the residual.

## Cache and compile options

`ExpressionCache` stores parsed trees, compiled lambda expressions, and compiled delegates. Tree and expression caches use `ExpressionCacheKey`; delegate caching uses lambda reference identity so the same cached lambda compiles once.

`ExpressionCacheKey.Create(language, source, contextType, resultType, options)` joins those five fields with the unit separator and hashes the material with SHA-256. `Parse` uses null context/result/options. `Compile` passes the target context type, result type, and an options fingerprint.

`ExpressionCompileOptions.Fingerprint(options, builtinsVersion)` returns a stable fragment for cache keys:

```text
builtins:v1;functions:none
builtins:v1;functions:name:runtimeHash,...
```

Compilers should pass a fingerprint whenever custom `ExpressionFunction` bindings affect output. AIP delegates this to `AipBuiltInFunctions.Fingerprint(options)`; CEL calls `ExpressionCompileOptions.Fingerprint(options)` directly.

## Dynamic values

`DynamicValues` centralizes alias-keyed row evaluation for dictionary contexts. Insight joins and Flow conditions can pass rows shaped like this:

```csharp
new Dictionary<string, object?> {
    ["o"] = new Dictionary<string, object?> {
        ["status"] = "paid",
        ["amount"] = 10L,
    },
};
```

The compilers emit calls to `DynamicValues.Member`, `Truthy`, `Equal`, comparison helpers, and arithmetic helpers when compiling against `IReadOnlyDictionary<string, object?>` or related map types.

Important semantics:

| Helper | Behavior |
| --- | --- |
| `Missing` | Sentinel distinct from a present null. Missing fields do not match equality or ordering. |
| `Truthy` | Booleans keep their value; present non-null values are true; missing and null are false. |
| `Equal` / `NotEqual` | Missing operands are false; two nulls are equal; numeric types compare by numeric value. |
| Numeric coercion | Integer, unsigned, floating, and decimal CLR numbers coerce through `double` for dynamic comparisons and arithmetic. |

CEL value mode builds on `DynamicValues` but routes full CEL scalar behavior through `CelValues`. See [CEL Expressions](cel.md).

## See also

- [Pushdown and Residual Evaluation](pushdown.md)
- [AIP Expressions](aip.md)
- [CEL Expressions](cel.md)
- [Custom Language](custom-language.md)
- [Resource Filtering](../resource/filtering.md)
