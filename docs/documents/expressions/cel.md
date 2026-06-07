# CEL Expressions

`CelCompiler` implements the [Common Expression Language (CEL)](https://github.com/google/cel-spec) filter language. It is built on the Parlot parser combinator library and registered as a keyed singleton under `CelLanguage.Name` ("cel"). CEL has no `IOrderCompiler` equivalent — order-by is not part of the CEL specification.

## Where the code lives

| Package | Key files |
|---|---|
| `Schemata.Expressions.Cel` | `CelLanguage.cs`, `CelParser.cs` |
| `Schemata.Expressions.Cel` | `CelCompiler.cs`, `CelCompileVisitor.cs` |
| `Schemata.Expressions.Cel` | `Expressions/CelNode.cs` (and siblings) |
| `Schemata.Expressions.Cel` | `ServiceCollectionExtensions.cs` |
| `specs/cel` | CEL conformance test suite (git submodule) |

## Registration

```csharp
services.AddCelExpressions();
// Registers:
//   IExpressionCompiler keyed "cel" -> CelCompiler (singleton)
```

CEL is not registered automatically by any Schemata feature. Call `AddCelExpressions()` explicitly when you need CEL filtering outside the resource system.

## Parser

`CelParser` is a static class with a single compiled Parlot parser:

- `CelParser.Expression` — parses a CEL expression string into a `CelNode` AST.

The parser is compiled once at class initialization. `CelNode` is the base type for all AST nodes; concrete subtypes include `CelConstant`, `CelIdentifier`, `CelMember`, `CelUnary`, `CelBinary`, `CelCall`, `CelMemberCall`, `CelConditional`, `CelList`, and `CelMap`.

## Supported CEL features

### Literals

| Type | Examples |
|---|---|
| Integer | `42`, `-7`, `0x1F` |
| Unsigned integer | `42u`, `0x1Fu` |
| Double | `3.14`, `-0.5`, `1e10` |
| Boolean | `true`, `false` |
| Null | `null` |
| String | `"hello"`, `'world'`, `"""triple"""`, `r"raw"` |
| Bytes | `b"bytes"` |

### Operators

| Operator | Symbol |
|---|---|
| Equality | `==` |
| Inequality | `!=` |
| Less than | `<` |
| Less than or equal | `<=` |
| Greater than | `>` |
| Greater than or equal | `>=` |
| Membership | `in` |
| Logical AND | `&&` |
| Logical OR | `\|\|` |
| Logical NOT | `!` |
| Arithmetic | `+`, `-`, `*`, `/`, `%` |
| Conditional | `? :` (ternary) |

### Member access and indexing

```
student.grade
student.address.city
```

Member names are resolved via `SchemataNaming.ToClrMemberName` (Pascalize), so `student.display_name` resolves to `Student.DisplayName`.

### Macros

CEL macros are implemented as member calls on collections:

| Macro | Signature | Compiles to |
|---|---|---|
| `exists` | `list.exists(x, predicate)` | `Enumerable.Any(list, x => predicate)` |
| `all` | `list.all(x, predicate)` | `Enumerable.All(list, x => predicate)` |
| `filter` | `list.filter(x, predicate)` | `Enumerable.Where(list, x => predicate).ToList()` |
| `map` | `list.map(x, transform)` | `Enumerable.Select(list, x => transform).ToList()` |

```cel
students.filter(s, s.grade > 3)
tags.exists(t, t == "urgent")
```

### Built-in functions

| Function | Behavior |
|---|---|
| `has(expr)` | Returns `true` if `expr` is non-null (reference types) or always `true` (value types) |
| `size(expr)` | Returns `string.Length`, array length, or `IEnumerable.Count()` |
| `contains(s)` | String contains; collection membership |
| `startsWith(s)` | String starts with |
| `endsWith(s)` | String ends with |
| `matches(pattern)` | Regex match with 100ms timeout |

### Lists and maps

```cel
[1, 2, 3].exists(x, x > 2)
{"key": "value"}
```

Lists compile to `List<object?>`. Maps compile to `Dictionary<object, object?>`.

## No `IOrderCompiler` (caveat #4)

CEL does not define an order-by syntax. `CelCompiler` implements only `IExpressionCompiler`, not `IOrderCompiler`. Calling `GetRequiredKeyedService<IOrderCompiler>(CelLanguage.Name)` will throw `InvalidOperationException`.

This also means CEL cannot be used for `ResourceOperationHandler.ListAsync` order-by, even if you register CEL as the filter compiler. The resource system's `ListAsync` is hard-wired to `AipLanguage.Name` for both filter and order. See [Filtering](../resource/filtering.md) for details.

## Conformance tests

The `specs/cel` directory is a git submodule pointing to the [google/cel-spec](https://github.com/google/cel-spec) repository. Conformance tests in `Schemata.Expressions.Cel` use the test vectors from this submodule.

To run conformance tests, first initialize the submodule:

```bash
git submodule update --init --recursive
```

Without the submodule, conformance tests will fail with file-not-found errors.

## Compilation

`CelCompiler.Parse` caches the AST by `ExpressionCacheKey.Create(language, source, null, null, null)`. `CelCompiler.Compile` does not cache the compiled lambda (unlike `AipCompiler`) — each call to `Compile` creates a new `CelCompileVisitor` and walks the AST. The tree cache still prevents re-parsing.

`CelCompileVisitor` walks the `CelNode` AST and builds LINQ expression nodes:

- `CelConstant` compiles to `Expression.Constant`.
- `CelIdentifier` resolves against the context parameter or local scopes (introduced by macros).
- `CelBinary` compiles to the corresponding `BinaryExpression`.
- `CelCall` handles `has` and `size` global functions.
- `CelMemberCall` handles macros (`exists`, `all`, `filter`, `map`) and string methods (`contains`, `startsWith`, `endsWith`, `matches`).
- `CelConditional` compiles to `Expression.Condition`.
- `CelList` compiles to `List<object?>` initialization.
- `CelMap` compiles to `Dictionary<object, object?>` initialization.

## Extension points

- CEL does not support custom functions via `ExpressionCompileOptions.Functions` (the visitor ignores the options parameter). To add custom functions, subclass `CelCompileVisitor` or implement a new `IExpressionCompiler` that wraps `CelCompiler`.
- To use CEL for filtering in a non-resource context, resolve `IExpressionCompiler` keyed by `CelLanguage.Name` and call `Parse`/`Compile` directly.

## Design motivation

CEL was added to support use cases where the AIP-160 filter language is insufficient — particularly for complex boolean logic, arithmetic, and collection operations. The Parlot-based implementation avoids a dependency on the official CEL Go runtime and keeps the stack entirely in .NET.

The decision to not implement `IOrderCompiler` for CEL reflects the CEL specification: CEL is an expression language, not a query language. Order-by is a query-level concern that belongs to AIP-132, not CEL.

## Caveats

- CEL has no `IOrderCompiler`. Order-by is not supported via CEL.
- `ResourceOperationHandler.ListAsync` is hard-wired to `AipLanguage.Name`. CEL cannot be used for resource filtering without modifying the handler.
- The `matches` function uses `Regex.IsMatch` with a 100ms timeout. Long-running regex patterns will throw `RegexMatchTimeoutException`.
- `CelCompiler.Compile` does not cache the compiled lambda. For hot paths, cache the compiled expression yourself or use `ExpressionCache.GetOrAddExpression` directly.
- Conformance tests require `git submodule update --init --recursive` to populate `specs/cel`.
- The `CelCompileVisitor` is `internal`. It cannot be subclassed from outside the package.

## See also

- [Expressions Overview](overview.md)
- [AIP Expressions](aip.md)
- [Custom Language](custom-language.md)
- [Filtering](../resource/filtering.md)
