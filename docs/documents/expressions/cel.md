# CEL Expressions

`CelCompiler` implements the [Common Expression Language (CEL)](https://github.com/google/cel-spec) as an `IExpressionCompiler`. It registers under `CelLanguage.Name` (`"cel"`), supports backend pushdown through `CelPushdownPlanner`, and exposes value-capable semantics through `CelValues`.

## Where the code lives

| Package                    | Key files                                                                                           |
| -------------------------- | --------------------------------------------------------------------------------------------------- |
| `Schemata.Expressions.Cel` | `CelLanguage.cs`, `CelParser.cs`                                                                    |
| `Schemata.Expressions.Cel` | `CelCompiler.cs`, `CelCompileVisitor.cs`, `CelValues.cs`                                            |
| `Schemata.Expressions.Cel` | `CelDuration.cs`, `CelTimestamp.cs`, `CelError.cs`, `CelType.cs`                                    |
| `Schemata.Expressions.Cel` | `CelPushdownPlanner.cs`, `ExpressionLanguageBuilderExtensions.cs`, `ServiceCollectionExtensions.cs` |
| `Schemata.Expressions.Cel` | `Expressions/CelNode.cs` and sibling AST nodes                                                      |

## Registration

Raw registration:

```csharp
services.AddCelExpressions(options => {
    options.Filtering = FilteringMode.Residual;
    options.MaxResidualScanRows = 25_000;
});
```

`AddCelExpressions` registers three keyed services under `CelLanguage.Name`:

| Service                        | Implementation / value                                                                                |
| ------------------------------ | ----------------------------------------------------------------------------------------------------- |
| `IExpressionCompiler`          | `CelCompiler`                                                                                         |
| `IExpressionPushdownPlanner`   | `CelPushdownPlanner`                                                                                  |
| `ExpressionLanguageDescriptor` | `Language = "cel"`, configured `Filtering`, configured `MaxResidualScanRows`, `SupportsValues = true` |

Modules should enable CEL through the builder seam:

```csharp
schema.UseResource()
      .UseCel(entry => entry.Filtering = FilteringMode.Residual)
      .UseOrdering();
```

`UseCel<T>(this T builder, Action<ExpressionLanguageEntry>? configure = null)` works over any `IExpressionLanguageBuilder`. It registers CEL services, enables `ExpressionLanguages.Cel` in the module profile, then applies entry-level overrides.

## Parser

`CelParser.Expression` is a compiled Parlot parser that produces a `CelNode` AST. Concrete nodes include `CelConstant`, `CelIdentifier`, `CelMember`, `CelIndex`, `CelUnary`, `CelBinary`, `CelCall`, `CelMemberCall`, `CelConditional`, `CelList`, and `CelMap`.

## Supported syntax

### Literals

| Type               | Examples                                       |
| ------------------ | ---------------------------------------------- |
| Integer / unsigned | `42`, `-7`, `0x1F`, `42u`                      |
| Double             | `3.14`, `-0.5`, `1e10`                         |
| Boolean / null     | `true`, `false`, `null`                        |
| String             | `"hello"`, `'world'`, `"""triple"""`, `r"raw"` |
| Bytes              | `b"bytes"`                                     |
| List / map         | `[1, 2]`, `{"name": "Alice"}`                  |

### Operators

| Category    | Symbols                          |
| ----------- | -------------------------------- |
| Comparison  | `==`, `!=`, `<`, `<=`, `>`, `>=` |
| Membership  | `in`                             |
| Logical     | `&&`, `\|\|`, `!`                |
| Arithmetic  | `+`, `-`, `*`, `/`, `%`          |
| Conditional | `? :`                            |

### Functions, member calls, and macros

| Syntax                             | Behavior                                                   |
| ---------------------------------- | ---------------------------------------------------------- |
| `has(expr)`                        | Presence check.                                            |
| `size(expr)`                       | Size of string, list, map, array, or countable collection. |
| `s.contains(x)`                    | String/list/map membership.                                |
| `s.startsWith(x)`, `s.endsWith(x)` | Ordinal string prefix/suffix checks.                       |
| `s.matches(pattern)`               | Regex match with a 100 ms timeout.                         |
| `list.exists(x, pred)`             | Existential macro.                                         |
| `list.all(x, pred)`                | Universal macro.                                           |
| `list.filter(x, pred)`             | Filtering macro.                                           |
| `list.map(x, expr)`                | Mapping macro.                                             |

Value mode also supports CEL conversion functions: `dyn`, `type`, `int`, `uint`, `double`, `string`, `bytes`, `bool`, `timestamp`, and `duration`.

## Value semantics

`CelValues.cs` is the value runtime. It implements CEL scalar, collection, error, timestamp, duration, conversion, macro, and member-call behavior over `object?` values. `CelDuration`, `CelTimestamp`, `CelError`, and `CelType` represent CEL-specific values.

The value families covered are:

- null;
- bool;
- int64 (`long`);
- uint64 (`ulong`);
- double;
- string;
- bytes (`byte[]`);
- list (`IReadOnlyList<object?>`);
- map (`IReadOnlyDictionary<object, object?>` using `CelValues.KeyComparer`);
- type (`CelType`);
- `Duration` (`CelDuration`);
- `Timestamp` (`CelTimestamp`);
- error (`CelError`).

`CelCompileVisitor` chooses value mode when the context type is `object`, `IReadOnlyDictionary<string, object?>`, `IDictionary<string, object?>`, or `IDictionary`. In value mode it emits calls to `CelValues.Identifier`, `Member`, `Has`, `And`, `Or`, arithmetic helpers, comparison helpers, `Contains`, `Index`, `List`, `Map`, macro helpers, conversion helpers, and `Call` for member functions. It still uses `DynamicValues.Member` for alias-keyed dictionary access under `CelValues.Member`.

When compiling a value-mode CEL expression to `bool`, `CelCompiler` wraps the result with `CelValues.IsTrue`. Only the CEL value `true` passes as a predicate; errors and other values do not become true.

CEL registers `SupportsValues: true`. Insight planning and Flow conditions can inspect the descriptor and route scalar/value evaluation through CEL, while AIP remains predicate-only.

## Dynamic row evaluation

Alias-keyed rows use nested dictionaries:

```csharp
var row = new Dictionary<string, object?> {
    ["o"] = new Dictionary<string, object?> {
        ["status"] = "paid",
        ["amount"] = 10L,
    },
};
```

A CEL expression like `o.status == 'paid'` compiles against `IReadOnlyDictionary<string, object?>`. Missing fields become `DynamicValues.Missing`, `has(o.email)` checks presence, and numeric comparisons follow `CelValues` numeric rules.

## Compilation

`CelCompiler.Parse` caches the AST keyed by `(language, source, null, null, null)`. `CelCompiler.Compile` caches the lambda through `ExpressionCache.GetOrAddExpression`, keyed by language, source, context type, result type, and `ExpressionCompileOptions.Fingerprint(options)`.

`CelCompileVisitor` has a typed mode for CLR objects and a value mode for dynamic/object contexts. Typed mode resolves members by CLR access and compiles LINQ expression operators. Value mode emits calls into `CelValues`.

## CEL pushdown

`CelPushdownPlanner` flattens top-level `&&` when `ExpressionCapabilities.Logical` is enabled. Flat identifiers, constants, simple comparisons, arithmetic with a flat field, `in`, `has(identifier)`, and selected string calls push when the matching capability flag is true. Navigation chains, macros, regex, indexes, conditionals, lists, and maps stay residual. See [Pushdown and Residual Evaluation](pushdown.md).

## Conformance tests

`tests/Schemata.Expressions.Cel.Tests/Conformance/CelSpecLoader.cs` reads CEL `.textproto` suites from `specs/cel/tests/simple/testdata/`. The loader covers the in-scope suite list in `CelSpecLoader.Suites`, parses each section and test, skips entries listed in `cel-spec-skips.txt`, and yields bindings plus typed expected values.

`CelSpecShould.Pass_InScopeConformanceVectors` parses each expression, compiles it against `IReadOnlyDictionary<string, object?>` to `object?`, evaluates it through the value-semantics machinery in `CelValues.cs`, and compares the result with the expected typed value. `Loads_InScopeConformanceCases` expects at least 850 in-scope cases, so the `specs/cel` submodule must be present.

## Order-by

CEL has no `IOrderCompiler`. Order-by is not part of CEL; use `Schemata.Expressions.Order` through `UseOrdering()` or `services.AddOrderExpressions()` for AIP-132 ordering alongside CEL filters.

## Caveats

- `matches` runs `Regex.IsMatch` with a 100 ms timeout; slow patterns surface a regex timeout error.
- The conformance suite requires the `specs/cel` submodule.
- Typed mode and value mode differ by context type. Use dictionary/object contexts when CEL value semantics are required.

## See also

- [Expressions Overview](overview.md)
- [Pushdown and Residual Evaluation](pushdown.md)
- [AIP Expressions](aip.md)
- [Custom Language](custom-language.md)
- [Resource Filtering](../resource/filtering.md)
