# CEL Expressions

`CelCompiler` implements the [Common Expression Language (CEL)](https://github.com/google/cel-spec) as an
`IExpressionCompiler`, built on the Parlot parser-combinator library and registered as a keyed singleton under
`CelLanguage.Name` (`"cel"`). CEL has no `IOrderCompiler`; order-by is not part of the language.

## Where the code lives

| Package | Key files |
| --- | --- |
| `Schemata.Expressions.Cel` | `CelLanguage.cs`, `CelParser.cs` |
| `Schemata.Expressions.Cel` | `CelCompiler.cs`, `CelCompileVisitor.cs` |
| `Schemata.Expressions.Cel` | `Expressions/CelNode.cs` and siblings, `ServiceCollectionExtensions.cs` |

## Registration

```csharp
services.AddCelExpressions();   // IExpressionCompiler keyed "cel" -> CelCompiler
```

CEL is not registered by any Schemata feature. Call `AddCelExpressions()` when you need CEL filtering outside the
resource system; the resource handler is fixed to AIP (see [Filtering](../resource/filtering.md)).

## Parser

`CelParser.Expression` is a single compiled Parlot parser that produces a `CelNode` AST. `CelNode` is the base
type; concrete nodes are `CelConstant`, `CelIdentifier`, `CelMember`, `CelUnary`, `CelBinary`, `CelCall`,
`CelMemberCall`, `CelConditional`, `CelList`, and `CelMap`.

## Supported features

### Literals

| Type | Examples |
| --- | --- |
| Integer / unsigned | `42`, `-7`, `0x1F`, `42u` |
| Double | `3.14`, `-0.5`, `1e10` |
| Boolean / null | `true`, `false`, `null` |
| String | `"hello"`, `'world'`, `"""triple"""`, `r"raw"` |
| Bytes | `b"bytes"` |

### Operators

| Category | Symbols |
| --- | --- |
| Comparison | `==`, `!=`, `<`, `<=`, `>`, `>=` |
| Membership | `in` |
| Logical | `&&`, `\|\|`, `!` |
| Arithmetic | `+`, `-`, `*`, `/`, `%` |
| Conditional | `? :` (ternary) |

### Member access

```
student.full_name
student.address.city
```

Member names resolve by Pascalizing (Humanizer `Pascalize()`), so `student.full_name` resolves to
`Student.FullName`.

### Global functions

| Function | Behavior |
| --- | --- |
| `has(expr)` | Presence check: non-null for reference types, key presence for maps |
| `size(expr)` | `string.Length`, array length, a `Count` property, or `Enumerable.Count()` |

### Member functions

| Function | Compiles to |
| --- | --- |
| `s.contains(x)` | `string.Contains`, or membership for collections/maps |
| `s.startsWith(x)` / `s.endsWith(x)` | `string.StartsWith` / `string.EndsWith` |
| `s.matches(pattern)` | `Regex.IsMatch` with a 100 ms timeout |
| `s.size()` | Same as `size(s)` |

### Macros

| Macro | Compiles to |
| --- | --- |
| `list.exists(x, pred)` | `Enumerable.Any(list, x => pred)` |
| `list.all(x, pred)` | `Enumerable.All(list, x => pred)` |
| `list.filter(x, pred)` | `Enumerable.Where(...).ToList()` |
| `list.map(x, expr)` | `Enumerable.Select(...).ToList()` |

```cel
scores.filter(score, score > 80).size() == 2
scores.exists(score, score > 90)
```

### Lists and maps

```cel
2 in [1, 2, 3]
"name" in {"name": "Alice"}
```

Lists compile to `List<object?>`; maps to `Dictionary<object, object?>`.

## Compilation

`CelCompiler.Parse` caches the AST keyed by `(language, source, null, null, null)`. `CelCompiler.Compile` caches
the lambda through `ExpressionCache.GetOrAddExpression`, keyed by language, source, context type, result type, and
a fingerprint of `ExpressionCompileOptions.Functions`. `CelCompileVisitor` walks the AST and receives the options,
so custom functions injected through `ExpressionCompileOptions.Functions` participate in both compilation and
cache keying.

## Conformance tests

`tests/Schemata.Expressions.Cel.Tests/Conformance` runs the upstream CEL spec corpus. `CelSpecLoader` reads
`specs/cel/tests/simple/testdata/basic.textproto`, yields the `self_eval_*` cases (skipping entries listed in
`cel-spec-skips.txt`), and `CelSpecShould` evaluates each expression and asserts the expected value. The
`specs/cel` directory is a submodule; populate it before running the suite:

```shell
git submodule update --init --recursive
```

## Extension points

- Inject custom functions through `ExpressionCompileOptions.Functions` when calling `Compile`.
- `CelCompileVisitor` is `internal`; to alter compilation, implement an `IExpressionCompiler` that wraps
  `CelCompiler`.
- Resolve `IExpressionCompiler` keyed by `CelLanguage.Name` and call `Parse`/`Compile` for CEL filtering in a
  non-resource context.

## Design rationale

The Parlot-based implementation keeps the stack in .NET without depending on the CEL Go runtime. CEL implements
only `IExpressionCompiler` because ordering is an AIP-132 query concern, not part of the expression language.

## Caveats

- CEL has no `IOrderCompiler`; resolving `IOrderCompiler` keyed by `CelLanguage.Name` throws.
- `matches` runs `Regex.IsMatch` with a 100 ms timeout; a slow pattern throws `RegexMatchTimeoutException`.
- The conformance suite requires the `specs/cel` submodule.

## See also

- [Expressions Overview](overview.md)
- [AIP Expressions](aip.md)
- [Custom Language](custom-language.md)
- [Filtering](../resource/filtering.md)
