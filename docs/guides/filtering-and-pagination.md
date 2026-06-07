# Filtering and Pagination

Filter, sort, and page the `Student` list endpoint using AIP-160 filter expressions and AIP-132 order-by syntax. No code changes are required — these features are built into the resource pipeline and work automatically on any list endpoint. This guide builds on [Getting Started](getting-started.md).

## Query parameters

`GET /students` accepts these query parameters:

| Parameter      | Type     | Description |
| -------------- | -------- | ----------- |
| `filter`       | `string` | AIP-160 filter expression |
| `order_by`     | `string` | Comma-separated ordering clauses (AIP-132) |
| `page_size`    | `int`    | Maximum results per page |
| `page_token`   | `string` | Continuation token from a previous `next_page_token` |
| `skip`         | `int`    | Number of results to skip before applying `page_size` |
| `show_deleted` | `bool`   | Include soft-deleted resources (default: `false`) |

## Filter syntax

Filter expressions follow [AIP-160](https://google.aip.dev/160). The `ResourceOperationHandler` parses them with the built-in AIP expression compiler (`AipLanguage.Name`). An invalid expression returns HTTP 422.

### Comparison operators

| Operator | Meaning | Example |
| -------- | ------- | ------- |
| `=`      | Equal | `age = 20` |
| `!=`     | Not equal | `age != 20` |
| `<`      | Less than | `age < 25` |
| `<=`     | Less than or equal | `age <= 25` |
| `>`      | Greater than | `age > 18` |
| `>=`     | Greater than or equal | `age >= 18` |
| `:`      | Has (contains / presence) | `full_name : "Ali"` |

The `:` operator on string fields acts as a substring check. On nullable fields, `field : *` checks for non-null.

### Logical operators

`AND`, `OR`, `NOT` (or `-`). Whitespace between terms is implicit `AND`. Parentheses group sub-expressions:

```text
(age > 18 AND age < 30) OR full_name = "Bob"
```

### Value types

| Type | Syntax | Example |
| ---- | ------ | ------- |
| String | Quoted or unquoted | `"Alice"`, `Alice` |
| Integer | Digits | `20` |
| Boolean | `TRUE` / `FALSE` | `TRUE` |
| Null | `NULL` | `NULL` |
| Wildcard | `*` (with `:`) | `field : *` |

Keywords are case-insensitive.

## Filter examples

```shell
# Students aged exactly 20
curl "http://localhost:5000/students?filter=age+%3D+20"

# Students whose full_name contains "Ali"
curl "http://localhost:5000/students?filter=full_name+%3A+%22Ali%22"

# Students older than 18 AND name contains "Bob"
curl "http://localhost:5000/students?filter=age+%3E+18+AND+full_name+%3A+%22Bob%22"
```

## Ordering

`order_by` accepts a comma-separated list of fields with optional `ASC` (default) or `DESC`:

```shell
# Sort by age descending, then full_name ascending
curl "http://localhost:5000/students?order_by=age+DESC%2Cfull_name+ASC"
```

## Pagination

List responses include:

| Field             | Description |
| ----------------- | ----------- |
| `students`        | The page of results |
| `total_size`      | Total count across all pages |
| `next_page_token` | Pass as `page_token` to fetch the next page; `null` when no more pages |

```shell
# First page of 2
curl "http://localhost:5000/students?page_size=2"
```

```json
{
  "students": [
    { "full_name": "Alice", "age": 20, "name": "students/..." },
    { "full_name": "Bob",   "age": 22, "name": "students/..." }
  ],
  "total_size": 4,
  "next_page_token": "eyJza..."
}
```

Pass `next_page_token` to continue:

```shell
curl "http://localhost:5000/students?page_size=2&page_token=eyJza..."
```

## Show deleted

Soft-deleted students are excluded by default. Pass `show_deleted=true` to include them:

```shell
curl "http://localhost:5000/students?show_deleted=true"
```

## Verify

```shell
# Seed students
curl -X POST http://localhost:5000/students -H "Content-Type: application/json" -d '{"full_name":"Alice","age":20}'
curl -X POST http://localhost:5000/students -H "Content-Type: application/json" -d '{"full_name":"Bob","age":22}'
curl -X POST http://localhost:5000/students -H "Content-Type: application/json" -d '{"full_name":"Charlie","age":19}'

# Filter age >= 20, ordered by age descending, page size 2
curl "http://localhost:5000/students?filter=age+%3E%3D+20&order_by=age+DESC&page_size=2"
```

## See also

- [Concurrency and Freshness](concurrency-and-freshness.md) — previous in the series: ETags and optimistic concurrency
- [Query Caching](query-caching.md) — next in the series: transparent query result caching with auto-eviction
- [Filtering](../documents/resource/filtering.md) — AIP-160 hard-wired compiler and expression stack
- [Expressions Overview](../documents/expressions/overview.md) — `IExpressionCompiler`, `ExpressionCache`
