# Filtering and Pagination

Filter, sort, and page the `Student` list endpoint with AIP-160 filter expressions and AIP-132 order-by syntax.
This guide follows [Concurrency and Freshness](concurrency-and-freshness.md). It updates the DTO-based resource
registration from [Object Mapping](object-mapping.md); if you skipped Object Mapping, keep `Use<Student>()` instead.

## Add the packages

The filter grammar and the order-by compiler live in their own packages:

```shell
dotnet add package --prerelease Schemata.Expressions.Aip
dotnet add package --prerelease Schemata.Expressions.Order
```

Enable both on the resource builder in `Program.cs`:

```csharp
schema.UseResource()
      .UseAip()
      .UseOrdering()
      .MapHttp()
      .Use<Student, StudentRequest, StudentDetail, StudentSummary>();
```

`UseAip()` registers the AIP-160 expression language and makes it the default filter language;
`UseOrdering()` registers the `IOrderCompiler` behind `order_by`. Without them, a `filter` value
fails to resolve a language and an `order_by` value fails to resolve the compiler. Pagination
works without either package.

## Query parameters

`GET /v1/students` accepts these query parameters:

| Parameter      | Type     | Description                                          |
| -------------- | -------- | ---------------------------------------------------- |
| `filter`       | `string` | AIP-160 filter expression                            |
| `order_by`     | `string` | Comma-separated clauses; AIP-132 uses an optional ` desc` suffix |
| `page_size`    | `int`    | Maximum results per page (default 25, capped at 100) |
| `page_token`   | `string` | Continuation token from a previous `next_page_token` |
| `skip`         | `int`    | Results to skip before applying `page_size`          |
| `show_deleted` | `bool`   | Include soft-deleted resources (default `false`)     |

## Filtering

[AIP-160](https://google.aip.dev/160) says a non-compliant filter should return `INVALID_ARGUMENT`
(HTTP `400`). Schemata instead wraps parsing and compilation failures in `ValidationException`, so
an invalid filter returns `INVALID_ARGUMENT` with HTTP `422`. This is Schemata's documented error-model
divergence, not an AIP-160 requirement.

| Operator  | Meaning                    | Example           |
| --------- | -------------------------- | ----------------- |
| `=`       | Equal                      | `age = 20`        |
| `!=`      | Not equal                  | `age != 20`       |
| `<`, `<=` | Less than (or equal)       | `age < 25`        |
| `>`, `>=` | Greater than (or equal)    | `age >= 18`       |
| `:`       | Has (substring / presence) | `full_name:"Ali"` |

Combine terms with `AND`, `OR`, and `NOT` (or `-`); adjacent terms are implicitly `AND`. Parentheses group
sub-expressions. Keywords are case-insensitive. On a string field, `:` is a substring check; on a nullable field,
`field:*` checks for a value.

```shell
# Students aged exactly 20
curl "http://localhost:5000/v1/students?filter=age%3D20"

# Students whose full_name contains "Ali"
curl "http://localhost:5000/v1/students?filter=full_name%3A%22Ali%22"

# Older than 18 AND name contains "Bob"
curl "http://localhost:5000/v1/students?filter=age%3E18%20AND%20full_name%3A%22Bob%22"
```

## Ordering

`order_by` is a comma-separated list of fields. AIP-132 makes ascending the default and specifies
the ` desc` suffix for descending order. Schemata also accepts `asc` case-insensitively as an
extension; it is not AIP-132 syntax.

```shell
# AIP-132 syntax: sort by age descending, then full_name ascending by default
curl "http://localhost:5000/v1/students?order_by=age%20DESC%2Cfull_name"

# Schemata extension: an explicit ASC token is also accepted
curl "http://localhost:5000/v1/students?order_by=age%20DESC%2Cfull_name%20ASC"
```

## Pagination

A list response carries the page, a total count, and a continuation token:

| Field             | Description                                                           |
| ----------------- | --------------------------------------------------------------------- |
| `students`        | The page of results                                                   |
| `total_size`      | Total count across all pages                                          |
| `next_page_token` | Pass as `page_token` for the next page; `null` when there are no more |

```shell
curl "http://localhost:5000/v1/students?page_size=2"
```

```json
{
  "students": [
    { "full_name": "Alice", "age": 20, "name": "students/..." },
    { "full_name": "Bob", "age": 22, "name": "students/..." }
  ],
  "total_size": 4,
  "next_page_token": "eyJza..."
}
```

The token is opaque and signed; pass it back verbatim to continue:

```shell
curl "http://localhost:5000/v1/students?page_size=2&page_token=eyJza..."
```

The `parent`, `filter`, `language`, `order_by`, and `show_deleted` values must stay the same across a paged run.
Schemata compares them with the values encoded in the token and raises `ValidationException` (HTTP `422`) on a
mismatch.

## Show deleted

Soft-deleted students are excluded by default. Pass `show_deleted=true` to include them:

```shell
curl "http://localhost:5000/v1/students?show_deleted=true"
```

## Verify

```shell
# Seed
curl -X POST http://localhost:5000/v1/students -H "Content-Type: application/json" -d '{"full_name":"Alice","age":20}'
curl -X POST http://localhost:5000/v1/students -H "Content-Type: application/json" -d '{"full_name":"Bob","age":22}'
curl -X POST http://localhost:5000/v1/students -H "Content-Type: application/json" -d '{"full_name":"Charlie","age":19}'

# Filter age >= 20, ordered by age descending, page size 2
curl "http://localhost:5000/v1/students?filter=age%3E%3D20&order_by=age%20DESC&page_size=2"
```

## Next steps

- [Query Caching](query-caching.md) — transparent caching for the list pipeline
- [Concurrency and Freshness](concurrency-and-freshness.md) — ETags on individual reads
- [Validation](validation.md) — guard request bodies on `POST`/`PATCH`

## See also

- [Filtering](../documents/resource/filtering.md) — the AIP-160 reference and the expression stack
- [AIP Expressions](../documents/expressions/aip.md) — the full filter and order grammar
