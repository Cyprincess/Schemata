# Filtering

The resource system implements the AIP-160 filtering specification. Filter expressions are parsed at request time into an AST and then compiled to LINQ `Expression<Func<T, bool>>` predicates applied to the entity query.

## Filter Parameter

Filters are passed via the `filter` query parameter on list requests:

```
GET /books?filter=status="published" AND price<20
```

In gRPC, the `Filter` property on `ListRequest` serves the same purpose.

## Grammar

The filter parser is built with the Parlot parsing library. The grammar follows the AIP-160 EBNF with these productions:

```
expression  = sequence { AND sequence }
sequence    = factor { factor }         -- implicit AND between adjacent factors
factor      = term { OR term }
term        = [NOT | "-"] simple
simple      = restriction | composite
composite   = "(" expression ")"
restriction = comparable [comparator arg]
comparable  = function | member
function    = path "(" [argList] ")"
path        = name { "." name }
member      = value { "." field }
field       = value | keyword
argList     = arg { "," arg }
arg         = comparable | composite
comparator  = "<=" | "<" | ">=" | ">" | "!=" | "=" | ":"
value       = INTEGER | NUMBER | TRUE | FALSE | NULL | TEXT | STRING
```

Keywords `AND`, `OR`, `NOT`, `TRUE`, `FALSE`, and `NULL` are case-insensitive and checked with word boundary detection (the next character must not be an identifier character).

## Comparison Operators

| Operator              | Symbol | Expression Type                     | Notes      |
| --------------------- | ------ | ----------------------------------- | ---------- |
| Equal                 | `=`    | `ExpressionType.Equal`              |            |
| Not Equal             | `!=`   | `ExpressionType.NotEqual`           |            |
| Less Than             | `<`    | `ExpressionType.LessThan`           |            |
| Less Than or Equal    | `<=`   | `ExpressionType.LessThanOrEqual`    |            |
| Greater Than          | `>`    | `ExpressionType.GreaterThan`        |            |
| Greater Than or Equal | `>=`   | `ExpressionType.GreaterThanOrEqual` |            |
| Has                   | `:`    | (special)                           | See below. |

For all operators except Has, if the right operand type does not match the left, it is automatically converted via `Expression.Convert`.

## The Has Operator

The `:` (has/contains) operator has special behavior depending on the left-hand type:

| Left-Hand Type             | Right-Hand Value | Behavior                                                                           |
| -------------------------- | ---------------- | ---------------------------------------------------------------------------------- |
| Any                        | `*` (wildcard)   | Presence check -- non-null for reference types, non-null-or-empty for collections. |
| `IDictionary`              | any              | Calls `ContainsKey` on the dictionary.                                             |
| `IEnumerable` (not string) | any              | Calls `Enumerable.Contains` on the collection.                                     |
| `string`                   | any              | Calls `string.Contains` for substring matching.                                    |
| Other                      | any              | Falls back to equality comparison.                                                 |

### Presence Check (`field:*`)

- For collections: `field != null && field.Any()`.
- For nullable types: `field != null`.
- For value types: always `true`.

## Logical Operators

| Operator | Syntax                                            | Behavior                                                                                        |
| -------- | ------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| AND      | `AND` keyword or implicit (space between factors) | Both conditions must be true. Adjacent terms without an explicit operator are implicitly ANDed. |
| OR       | `OR` keyword                                      | Either condition must be true.                                                                  |
| NOT      | `NOT` keyword or `-` prefix                       | Negates the following term.                                                                     |

Precedence (highest to lowest):

1. NOT / `-`
2. OR
3. Implicit AND (adjacent factors in a sequence)
4. Explicit AND

Parentheses `()` can override precedence.

## Value Types

| Type          | Syntax                              | Examples                  |
| ------------- | ----------------------------------- | ------------------------- |
| Integer       | Decimal digits (no dot following)   | `42`, `0`, `-1`           |
| Number        | Decimal with dot                    | `3.14`, `0.5`             |
| Boolean       | `TRUE` / `FALSE` (case-insensitive) | `true`, `FALSE`           |
| Null          | `NULL` (case-insensitive)           | `null`                    |
| Unquoted text | Identifier characters               | `active`, `hello_world`   |
| Quoted string | Double-quoted                       | `"hello world"`, `"it's"` |

The integer parser uses a look-ahead to distinguish integers from numbers: if a dot followed by a digit appears after the integer, it falls through to the number parser.

## Member Access

Members reference entity properties. They support dot-separated paths:

```
author.name = "Tolkien"
metadata.tags : "fiction"
```

Property names are resolved by converting to snake_case via Humanizer's `Underscore()` method. When the filter container is built, all writable properties of the entity type are bound with their snake_case names. The entity itself is bound as a parameter with the singularized, underscored type name.

For example, for a `BookReview` entity:

- `book_review` is the parameter name.
- `title` resolves to `BookReview.Title`.
- `create_time` resolves to `BookReview.CreateTime`.

## Custom Functions

The filter grammar supports function call syntax:

```
contains(title, "adventure")
time.now()
```

Functions must be registered on the `Container` before expression building. The `ResourceRequestContainer<T>.FilterConfigure` property provides a hook:

```csharp
container.FilterConfigure = c => {
    c.RegisterFunction("contains", (args, ctx) =>
        Expression.Call(args[0], "Contains", null, args[1]));

    c.RegisterFunction("startsWith", (args, ctx) =>
        Expression.Call(args[0], "StartsWith", null, args[1]));
};
```

Function resolution follows two strategies:

1. **Full name lookup**: The full dotted path (e.g., `time.now`) is looked up in the registered functions dictionary.
2. **Instance-style fallback**: If the path has multiple segments, the last segment is tried as the function name with the preceding path treated as the first argument. For example, `msg.endsWith("!")` looks up `endsWith` and passes the `msg` member expression as the first argument.

If no registered function matches, a `ParseException` is thrown.

## Ordering

The `orderBy` parameter specifies how results should be sorted:

```
GET /books?orderBy=create_time desc, title
```

### Grammar

```
order = member [ASC | DESC] { "," member [ASC | DESC] }
```

Each member is parsed the same way as filter members. The direction keyword is case-insensitive:

| Direction  | Keyword            | Enum Value            |
| ---------- | ------------------ | --------------------- |
| Ascending  | `ASC` (or omitted) | `Ordering.Ascending`  |
| Descending | `DESC`             | `Ordering.Descending` |

Multiple order clauses are comma-separated. They are applied in order, with the first becoming `OrderBy` and subsequent ones becoming `ThenBy`.

Property resolution works the same as for filters -- snake_case names are mapped to entity properties.

### Application

`QueryableExtensions.WithOrdering` applies each ordering to the query:

- If the source is not yet ordered, uses `OrderBy` / `OrderByDescending`.
- If the source is already ordered (`IOrderedQueryable`), chains with `ThenBy` / `ThenByDescending`.

## Pagination

Pagination uses two complementary mechanisms:

### Page Size and Skip

| Parameter  | Default | Range  | Description                                                             |
| ---------- | ------- | ------ | ----------------------------------------------------------------------- |
| `pageSize` | 25      | 1--100 | Maximum items per page. Values <= 0 become 25; values > 100 become 100. |
| `skip`     | 0       | >= 0   | Number of items to skip. Added to the page token's accumulated offset.  |

### Page Token

The `pageToken` is a Brotli-compressed, Base64 URL-safe encoded JSON object containing:

| Field         | Description                                      |
| ------------- | ------------------------------------------------ |
| `Parent`      | The parent value from the original request.      |
| `Filter`      | The filter from the original request.            |
| `OrderBy`     | The ordering from the original request.          |
| `ShowDeleted` | The show-deleted flag from the original request. |
| `PageSize`    | The page size.                                   |
| `Skip`        | The accumulated skip offset.                     |

On each page, the handler validates that the token's `Parent`, `Filter`, `OrderBy`, and `ShowDeleted` match the current request. A mismatch throws `InvalidArgumentException` with field `"page_token"` -- this prevents clients from changing query parameters while paging.

The `nextPageToken` is generated when the result count is greater than or equal to the page size (indicating more results may exist). The skip offset is advanced by `PageSize` and the token is re-serialized.

### Query Execution

The handler counts total matching entities first (before pagination), then applies skip/take:

```csharp
var totalSize = await repository.CountAsync(q => container.Query(q), ct);
container.ApplyPaginating(token);
var entities = repository.ListAsync(q => container.Query(q), ct);
```

The `TotalSize` field in `ListResult` reflects the count before pagination, allowing clients to know how many total results match the query.

## Filter Examples

```
// Simple equality
status = "active"

// Numeric comparison
price >= 10 AND price < 50

// String containment
title : "adventure"

// Presence check
description : *

// Collection contains
tags : "fiction"

// Negation
NOT status = "archived"
-status = "archived"

// Complex expression with grouping
(status = "active" OR status = "pending") AND create_time >= "2024-01-01"

// Ordering with filter
GET /books?filter=status="active"&orderBy=create_time desc, title
```
