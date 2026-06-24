# AIP Expressions

`AipCompiler` implements the AIP-160 filter language. `AipPushdownPlanner` can split AIP filters for backend pushdown and local residual evaluation. AIP-132 order-by now lives in `Schemata.Expressions.Order`, not in the AIP package.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Expressions.Aip` | `AipLanguage.cs`, `AipParser.cs` |
| `Schemata.Expressions.Aip` | `AipCompiler.cs`, `AipCompileVisitor.cs`, `AipBuiltInFunctions.cs` |
| `Schemata.Expressions.Aip` | `AipPushdownPlanner.cs`, `ExpressionLanguageBuilderExtensions.cs`, `ServiceCollectionExtensions.cs` |
| `Schemata.Expressions.Order` | `OrderCompiler.cs`, `ServiceCollectionExtensions.cs` |

## Registration

Raw service registration is still available:

```csharp
services.AddAipExpressions(options => {
    options.Filtering = FilteringMode.Residual;
    options.MaxResidualScanRows = 10_000;
});
```

`AddAipExpressions` registers three keyed services under `AipLanguage.Name` (`"aip"`):

| Service | Implementation / value |
| --- | --- |
| `IExpressionCompiler` | `AipCompiler` |
| `IExpressionPushdownPlanner` | `AipPushdownPlanner` |
| `ExpressionLanguageDescriptor` | `Language = "aip"`, configured `Filtering`, configured `MaxResidualScanRows`, `SupportsValues = false` |

Modules should prefer the language-builder seam:

```csharp
schema.UseResource()
      .UseAip(entry => {
          entry.Filtering = FilteringMode.Residual;
          entry.MaxResidualScanRows = 20_000;
      })
      .UseOrdering();
```

`UseAip<T>(this T builder, Action<ExpressionLanguageEntry>? configure = null)` works over any `IExpressionLanguageBuilder`. It registers AIP services, enables `ExpressionLanguages.Aip` on the module profile, then applies entry-level overrides.

## Parser

`AipParser.Filter` is a compiled Parlot parser that produces a `Filter` AST node. The parser is built once at class initialization.

## Filter grammar

```text
filter      = sequence (AND sequence)*
sequence    = factor+
factor      = term (OR term)*
term        = [NOT | -] simple
simple      = restriction | composite
composite   = "(" filter ")"
restriction = comparable [comparator arg]
comparable  = function | member
function    = path "(" [args] ")"
member      = value ("." field)*
comparator  = "<=" | "<" | ">=" | ">" | "!=" | "=" | ":"
value       = integer | number | TRUE | FALSE | NULL | unquoted | quoted
```

### Operators

| Operator | Symbol | Notes |
| --- | --- | --- |
| Equality | `=` | String and numeric equality. |
| Inequality | `!=` | String and numeric inequality. |
| Less / less-or-equal | `<`, `<=` | Numeric comparison. |
| Greater / greater-or-equal | `>`, `>=` | Numeric comparison. |
| Has | `:` | Substring on strings, membership on collections, presence with `*`. |
| Logical AND | `AND` | Case-insensitive; adjacent terms are ANDed. |
| Logical OR | `OR` | Case-insensitive. |
| Logical NOT | `NOT`, `-` | Case-insensitive. |

`AND` binds tighter than `OR`; adjacent terms with no explicit operator are implicitly ANDed.

### Literals

| Type | Examples |
| --- | --- |
| Integer | `42`, `-7` |
| Number | `3.14`, `-0.5` |
| Boolean | `TRUE`, `FALSE` |
| Null | `NULL` |
| Unquoted string | `alice`, `les-miserables` |
| Quoted string | `"hello world"` |

### Member access

Members are dot paths: `author.display_name`. Each segment resolves against the context type through `MemberAccess.Resolve`, which accepts common CLR and wire-name forms.

## Order-by is language-independent

AIP-132 order-by is implemented by `Schemata.Expressions.Order.OrderCompiler` and registered with `UseOrdering()` or `services.AddOrderExpressions()`. CEL, AIP, and custom filter languages all use the same non-keyed `IOrderCompiler` when a module enables ordering. See [Expressions Overview](overview.md) for the contract.

## Built-in functions

`AipBuiltInFunctions.Resolve` checks `ExpressionCompileOptions.Functions` first, then the built-ins:

| Function | Argument | Returns |
| --- | --- | --- |
| `timestamp(s)` | One string literal | `DateTime.Parse(s, InvariantCulture, RoundtripKind)` |
| `duration(s)` | One string literal | `TimeSpan` summed from `h`/`m`/`s` units |

```text
create_time > timestamp("2024-01-01T00:00:00Z")
elapsed > duration("1h30m")
```

Inject custom functions when calling `Compile` directly:

```csharp
var options = new ExpressionCompileOptions();
options.Functions["now"] = new ExpressionFunction(_ => Expression.Constant(DateTime.UtcNow));
var expr = compiler.Compile<Student, bool>(tree, options);
```

AIP is predicate-only. Its descriptor registers `SupportsValues: false`, and dynamic dictionary evaluation returns boolean predicates through `DynamicValues` rather than scalar CEL values.

## Compilation

`AipCompiler.Parse` caches the AST keyed by `(language, source, null, null, null)`. `AipCompiler.Compile` caches the lambda keyed by language, source, context type, result type, and `AipBuiltInFunctions.Fingerprint(options)`. `AipCompileVisitor` walks the AST: restrictions become comparison expressions, `:` becomes presence or membership logic, functions resolve through `AipBuiltInFunctions.Resolve`, members resolve through `MemberAccess.Resolve`, and `NOT`/`AND`/`OR` become `Expression.Not`/`AndAlso`/`OrElse`.

When compiling against `IReadOnlyDictionary<string, object?>`, AIP uses `DynamicValues.Member`, `Truthy`, `Equal`, and comparison helpers for alias-keyed rows.

## AIP pushdown

`AipPushdownPlanner` splits top-level conjunctions. Flat-field comparisons, presence checks, membership checks, and wildcard equality push when the supplied `ExpressionCapabilities` allow them. Navigation chains, functions, and unsafe disjunctions stay residual. See [Pushdown and Residual Evaluation](pushdown.md).

## Error handling

`Parse` and `Compile` wrap Parlot parse failures in `ExpressionException`; invalid tree types and member paths raise `ArgumentException` or `ParseException`. `ResourceOperationHandler.ListAsync` maps expression failures to `ValidationException` with `FieldReasons.InvalidFilter`. Order-by failures from `OrderCompiler` map to `FieldReasons.InvalidOrderBy`.

## Caveats

- `duration` accepts integer counts before `h`/`m`/`s`; a fractional count such as `"1.5h"` raises `ParseException`.
- `timestamp` parses with `DateTimeStyles.RoundtripKind`; malformed strings fail at compile time.
- A function argument must be a string literal; nested calls like `timestamp(now())` are not parsed.

## See also

- [Expressions Overview](overview.md)
- [Pushdown and Residual Evaluation](pushdown.md)
- [CEL Expressions](cel.md)
- [Custom Language](custom-language.md)
- [Resource Filtering](../resource/filtering.md)
