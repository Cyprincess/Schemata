# AIP Expressions

`AipCompiler` and `AipOrderCompiler` implement the AIP-160 filter language and AIP-132 order-by language respectively. Both are built on the Parlot parser combinator library and registered as keyed singletons under `AipLanguage.Name` ("aip"). `SchemataResourceFeature` registers them automatically via `services.AddAipExpressions()`.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Expressions.Aip` | `AipLanguage.cs`, `AipParser.cs` |
| `Schemata.Expressions.Aip` | `AipCompiler.cs`, `AipCompileVisitor.cs` |
| `Schemata.Expressions.Aip` | `AipOrderCompiler.cs` |
| `Schemata.Expressions.Aip` | `AipBuiltInFunctions.cs` |
| `Schemata.Expressions.Aip` | `ServiceCollectionExtensions.cs` |

## Registration

```csharp
services.AddAipExpressions();
// Registers:
//   IExpressionCompiler keyed "aip" -> AipCompiler (singleton)
//   IOrderCompiler keyed "aip"      -> AipOrderCompiler (singleton)
```

`SchemataResourceFeature` calls this automatically. You only need to call it manually when using the AIP compiler outside the resource system.

## Parser

`AipParser` is a static class with two compiled Parlot parsers:

- `AipParser.Filter` — parses a filter string into a `Filter` AST node.
- `AipParser.Order` — parses an order-by string into a list of `(Member, Ordering)` pairs.

Both parsers are compiled once at class initialization and reused for all subsequent parses. The `Filter` parser is built from a recursive grammar that handles the full AIP-160 expression language.

## Filter grammar

The AIP-160 grammar supported by `AipParser` is:

```text
filter     = sequence (AND sequence)*
sequence   = factor+
factor     = term (OR term)*
term       = [NOT | -] simple
simple     = restriction | composite
composite  = "(" filter ")"
restriction = comparable [comparator arg]
comparable  = function | member
function    = path "(" [args] ")"
member      = value ("." field)*
comparator  = "<=" | "<" | ">=" | ">" | "!=" | "=" | ":"
arg         = comparable | composite
value       = integer | number | TRUE | FALSE | NULL | unquoted | quoted
```

### Operators

| Operator | Symbol | Notes |
|---|---|---|
| Equality | `=` | String and numeric equality |
| Inequality | `!=` | String and numeric inequality |
| Less than | `<` | Numeric comparison |
| Less than or equal | `<=` | Numeric comparison |
| Greater than | `>` | Numeric comparison |
| Greater than or equal | `>=` | Numeric comparison |
| Has | `:` | Substring match on strings; membership on collections |
| Logical AND | `AND` | Case-insensitive; implicit between adjacent terms |
| Logical OR | `OR` | Case-insensitive |
| Logical NOT | `NOT` | Case-insensitive |
| Negation | `-` | Equivalent to `NOT` |

`AND` and `OR` are keyword-bounded — they must be surrounded by non-identifier characters. `AND` has higher precedence than `OR`; adjacent terms (no explicit operator) are implicitly ANDed.

### Literals

| Type | Examples |
|---|---|
| Integer | `42`, `-7` |
| Decimal | `3.14`, `-0.5` |
| Boolean | `TRUE`, `FALSE` (case-insensitive) |
| Null | `NULL` (case-insensitive) |
| Unquoted string | `alice`, `les-miserables` |
| Quoted string | `"hello world"`, `"it's a test"` |

Unquoted strings match identifier characters and Unicode code points above 127. Keywords (`AND`, `OR`, `NOT`, `TRUE`, `FALSE`, `NULL`) are excluded from unquoted strings.

### Member access

Members are dot-separated paths: `author.name`, `address.city`. Each segment is resolved against the context type via `SchemataNaming.ToClrMemberName` (Pascalize), so `author.display_name` resolves to `Author.DisplayName` on the entity.

## Order-by grammar

```text
order  = item ("," item)*
item   = member [ASC | DESC]
```

Default direction is ascending. `ASC` and `DESC` are case-insensitive and keyword-bounded.

```
grade DESC, name ASC
```

## Built-in functions

`AipBuiltInFunctions` provides two built-in functions:

| Function | Signature | Returns |
|---|---|---|
| `timestamp(s)` | One string literal argument | `DateTime` parsed with `DateTimeStyles.RoundtripKind` |
| `duration(s)` | One string literal argument | `TimeSpan` parsed from `h`/`m`/`s` units |

```
create_time > timestamp("2024-01-01T00:00:00Z")
age > duration("1h30m")
```

Duration units: `h` (hours), `m` (minutes), `s` (seconds). Units can be combined: `"1h30m"`, `"90s"`.

Custom functions can be injected via `ExpressionCompileOptions.Functions`:

```csharp
var options = new ExpressionCompileOptions();
options.Functions["now"] = new ExpressionFunction(args =>
    Expression.Constant(DateTime.UtcNow));

var expr = compiler.Compile<Student, bool>(tree, options);
```

## Compilation

`AipCompiler.Parse` caches the AST by `ExpressionCacheKey.Create(language, source, null, null, null)`. `AipCompiler.Compile` caches the compiled lambda by a key that includes the context type, result type, and a fingerprint of any custom functions.

`AipCompileVisitor` walks the AST and builds LINQ expression nodes:

- `Restriction` nodes compile to `BinaryExpression` (e.g., `Equal`, `LessThan`).
- `Has` (`:`) compiles to `string.Contains` for string members, `Enumerable.Contains` for collections.
- `Function` nodes are resolved via `AipBuiltInFunctions.Resolve` or `options.Functions`.
- `Member` nodes resolve property paths via `SchemataNaming.ToClrMemberName` (Pascalize).
- `NOT`/`-` compile to `Expression.Not`.
- `AND`/`OR` compile to `Expression.AndAlso`/`Expression.OrElse`.

## Order compilation

`AipOrderCompiler.CompileOrder<T>` parses the order string and builds a chain of `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending` calls via reflection on `Queryable`. The first field uses `OrderBy`; subsequent fields use `ThenBy`.

## Error handling

Both `Parse` and `CompileOrder` throw `ArgumentException` on invalid input. `ResourceOperationHandler.ListAsync` catches `ParseException` and `ArgumentException` and converts them to `ValidationException` with `FieldReasons.InvalidFilter` or `FieldReasons.InvalidOrderBy`.

## Extension points

- Inject custom functions via `ExpressionCompileOptions.Functions` when calling `Compile` directly.
- The `AipCompileVisitor` is `internal`. To extend compilation behavior, implement a new `IExpressionCompiler` that wraps `AipCompiler` and post-processes the resulting expression.

## Design motivation

Parlot was chosen for its zero-allocation parser combinator design and its ability to compile parsers to delegates at initialization time. The static `AipParser.Filter` and `AipParser.Order` fields are compiled once and reused, avoiding per-request parser construction overhead.

Member resolution via `SchemataNaming.ToClrMemberName` (Pascalize) maps snake_case wire names straight to PascalCase CLR property names, so the same filter string works against your entities directly.

## Caveats

- The `has` operator (`:`) on a non-string, non-collection property will throw `ParseException` at compile time with "Type is not enumerable."
- Wildcard `*` in a `has` expression (e.g., `name:*`) compiles to a presence check (not-null for reference types, always-true for value types). It does not support glob patterns.
- `timestamp(s)` parses with `DateTimeStyles.RoundtripKind`. Timezone-naive strings are treated as UTC. Malformed strings throw `ParseException` at compile time.
- `duration(s)` supports only `h`, `m`, `s` units. Fractional values are supported (e.g., `"1.5h"`). Other units (days, weeks) are not supported and throw `ParseException`.
- The AIP compiler does not support nested function calls (e.g., `timestamp(now())`). Function arguments must be string literals.

## See also

- [Expressions Overview](overview.md)
- [CEL Expressions](cel.md)
- [Custom Language](custom-language.md)
- [Filtering](../resource/filtering.md)
