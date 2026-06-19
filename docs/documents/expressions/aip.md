# AIP Expressions

`AipCompiler` implements the AIP-160 filter language and `AipOrderCompiler` the AIP-132 order-by language. Both
are built on the Parlot parser-combinator library and register as keyed singletons under `AipLanguage.Name`
(`"aip"`). `SchemataResourceFeature` registers them through `services.AddAipExpressions()`.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Expressions.Aip` | `AipLanguage.cs`, `AipParser.cs` |
| `Schemata.Expressions.Aip` | `AipCompiler.cs`, `AipCompileVisitor.cs`, `AipOrderCompiler.cs` |
| `Schemata.Expressions.Aip` | `AipBuiltInFunctions.cs`, `Ordering.cs`, `ServiceCollectionExtensions.cs` |

## Registration

```csharp
services.AddAipExpressions();
//   IExpressionCompiler keyed "aip" -> AipCompiler
//   IOrderCompiler      keyed "aip" -> AipOrderCompiler
```

## Parser

`AipParser` holds two compiled Parlot parsers, built once at class initialization:

- `AipParser.Filter` parses a filter string into a `Filter` AST node.
- `AipParser.Order` parses an order-by string into a list of `(Member, Ordering)` pairs.

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
| Equality | `=` | String and numeric equality |
| Inequality | `!=` | String and numeric inequality |
| Less / less-or-equal | `<`, `<=` | Numeric comparison |
| Greater / greater-or-equal | `>`, `>=` | Numeric comparison |
| Has | `:` | Substring on strings, membership on collections |
| Logical AND | `AND` | Case-insensitive; implicit between adjacent terms |
| Logical OR | `OR` | Case-insensitive |
| Logical NOT | `NOT`, `-` | Case-insensitive |

`AND` binds tighter than `OR`; adjacent terms with no explicit operator are implicitly ANDed.

### Literals

| Type | Examples |
| --- | --- |
| Integer | `42`, `-7` |
| Number | `3.14`, `-0.5` |
| Boolean | `TRUE`, `FALSE` (case-insensitive) |
| Null | `NULL` (case-insensitive) |
| Unquoted string | `alice`, `les-miserables` |
| Quoted string | `"hello world"` |

### Member access

Members are dot paths: `author.display_name`. Each segment is resolved against the context type by Pascalizing
(Humanizer `Pascalize()`), so `author.display_name` resolves to `Author.DisplayName`.

## Order-by grammar

```text
order = item ("," item)*
item  = member [ASC | DESC]
```

Default direction is ascending; `ASC`/`DESC` are case-insensitive. `Ordering` is `{ Ascending, Descending }`.
`AipOrderCompiler.CompileOrder<T>` builds an `OrderBy`/`OrderByDescending`/`ThenBy`/`ThenByDescending` chain over
`Queryable`. Member paths may be nested (`foo.bar`).

```
grade DESC, full_name ASC
```

## Built-in functions

`AipBuiltInFunctions.Resolve` checks `options.Functions` first, then the two built-ins:

| Function | Argument | Returns |
| --- | --- | --- |
| `timestamp(s)` | One string literal | `DateTime.Parse(s, InvariantCulture, RoundtripKind)` |
| `duration(s)` | One string literal | `TimeSpan` summed from `h`/`m`/`s` units |

```
create_time > timestamp("2024-01-01T00:00:00Z")
elapsed > duration("1h30m")
```

`duration` reads an integer count followed by a unit (`h`, `m`, `s`), repeated; e.g. `"1h30m"`, `"90s"`.

Inject custom functions through `ExpressionCompileOptions.Functions`:

```csharp
var options = new ExpressionCompileOptions();
options.Functions["now"] = new ExpressionFunction(_ => Expression.Constant(DateTime.UtcNow));
var expr = compiler.Compile<Student, bool>(tree, options);
```

## Compilation

`AipCompiler.Parse` caches the AST keyed by `(language, source, null, null, null)`. `AipCompiler.Compile` caches
the lambda keyed by language, source, context type, result type, and `AipBuiltInFunctions.Fingerprint(options)`.
`AipCompileVisitor` walks the AST: restrictions become comparison expressions, `:` becomes a `Contains` call,
functions resolve through `AipBuiltInFunctions.Resolve`, members resolve by Pascalizing, and `NOT`/`AND`/`OR`
become `Expression.Not`/`AndAlso`/`OrElse`.

## Error handling

`Parse` and `CompileOrder` throw `ArgumentException` (and Parlot's `ParseException`) on invalid input.
`ResourceOperationHandler.ListAsync` converts both to `ValidationException` with `FieldReasons.InvalidFilter` or
`FieldReasons.InvalidOrderBy`.

## Extension points

- Inject custom functions via `ExpressionCompileOptions.Functions` when calling `Compile` directly.
- `AipCompileVisitor` is `internal`; to alter compilation, implement an `IExpressionCompiler` that wraps
  `AipCompiler` and post-processes the result.

## Design rationale

Parlot compiles its parsers to delegates at initialization, so the static `AipParser.Filter` and `AipParser.Order`
fields avoid per-request parser construction. Pascalizing member segments maps snake_case wire names straight to
PascalCase CLR properties.

## Caveats

- `duration` accepts only integer counts before `h`/`m`/`s`; a fractional count such as `"1.5h"` raises
  `ParseException`. Day and week units are unsupported.
- `timestamp` parses with `DateTimeStyles.RoundtripKind`; a malformed string throws at compile time.
- A function argument must be a string literal; nested calls such as `timestamp(now())` are not parsed.

## See also

- [Expressions Overview](overview.md)
- [CEL Expressions](cel.md)
- [Custom Language](custom-language.md)
- [Filtering](../resource/filtering.md)
