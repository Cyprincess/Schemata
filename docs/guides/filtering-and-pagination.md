# Filtering and Pagination

This guide builds on [Concurrency and Freshness](concurrency-and-freshness.md). Filtering, ordering, and pagination are built into the resource pipeline and work automatically on any list endpoint.

## Query parameters

The `GET /students` endpoint accepts the following query parameters via the `ListRequest` class:

| Parameter      | Type     | Description                                                     |
| -------------- | -------- | --------------------------------------------------------------- |
| `parent`       | `string` | Parent resource path (auto-resolved from route if not provided) |
| `filter`       | `string` | Filter expression using AIP-160 syntax                          |
| `order_by`     | `string` | Comma-separated ordering clauses                                |
| `page_size`    | `int`    | Maximum number of results per page                              |
| `page_token`   | `string` | Continuation token from a previous response's `next_page_token` |
| `skip`         | `int`    | Number of results to skip                                       |
| `show_deleted` | `bool`   | Include soft-deleted resources (default: `false`)               |

## Filter syntax

The filter grammar follows the [AIP-160](https://google.aip.dev/160) specification. Filter expressions are passed as the `filter` query parameter.

### Comparison operators

| Operator | Meaning                   | Example             |
| -------- | ------------------------- | ------------------- |
| `=`      | Equal                     | `age = 20`          |
| `!=`     | Not equal                 | `age != 20`         |
| `<`      | Less than                 | `age < 25`          |
| `<=`     | Less than or equal        | `age <= 25`         |
| `>`      | Greater than              | `age > 18`          |
| `>=`     | Greater than or equal     | `age >= 18`         |
| `:`      | Has (contains / presence) | `full_name : "Ali"` |

### The has operator (`:`)

The `:` operator behaves differently depending on the field type:

- **String fields**: acts as a substring contains check (`full_name : "Ali"` matches `"Alice"`)
- **Collection fields**: checks if the collection contains the value
- **Nullable fields**: `field : *` checks that the field is not null
- **Other types**: treated as equality

### Logical operators

| Operator     | Meaning                                                       |
| ------------ | ------------------------------------------------------------- |
| `AND`        | Logical and (implicit when terms are separated by whitespace) |
| `OR`         | Logical or                                                    |
| `NOT` or `-` | Negation (prefix)                                             |

Parentheses group sub-expressions: `(age > 18 AND age < 30) OR full_name = "Bob"`.

Whitespace between terms is treated as implicit `AND`:

```
full_name : "Ali" age > 18
```

is equivalent to:

```
full_name : "Ali" AND age > 18
```

### Value types

| Type     | Syntax                              | Examples           |
| -------- | ----------------------------------- | ------------------ |
| String   | Quoted or unquoted                  | `"Alice"`, `Alice` |
| Integer  | Digits                              | `20`               |
| Decimal  | Digits with dot                     | `3.14`             |
| Boolean  | `TRUE` / `FALSE` (case-insensitive) | `TRUE`             |
| Null     | `NULL` (case-insensitive)           | `NULL`             |
| Wildcard | `*` (used with `:` for presence)    | `field : *`        |

Keywords (`AND`, `OR`, `NOT`, `TRUE`, `FALSE`, `NULL`) are case-insensitive.

## Filter examples

```shell
# Students aged exactly 20
curl "http://localhost:5000/students?filter=age%20%3D%2020"

# Students whose full_name contains "Ali"
curl "http://localhost:5000/students?filter=full_name%20%3A%20%22Ali%22"

# Students older than 18 AND name contains "Bob"
curl "http://localhost:5000/students?filter=age%20%3E%2018%20AND%20full_name%20%3A%20%22Bob%22"

# Students aged 20 OR aged 25
curl "http://localhost:5000/students?filter=age%20%3D%2020%20OR%20age%20%3D%2025"

# Students who are NOT aged 20
curl "http://localhost:5000/students?filter=NOT%20age%20%3D%2020"
```

For readability, the raw filter strings before URL encoding:

```
age = 20
full_name : "Ali"
age > 18 AND full_name : "Bob"
age = 20 OR age = 25
NOT age = 20
```

## Ordering

Use `order_by` with a comma-separated list of fields. Each field can have an optional `ASC` (ascending, default) or `DESC` (descending) suffix.

```shell
# Sort by age ascending (default)
curl "http://localhost:5000/students?order_by=age"

# Sort by age descending
curl "http://localhost:5000/students?order_by=age%20DESC"

# Sort by full_name ascending, then by age descending
curl "http://localhost:5000/students?order_by=full_name%20ASC%2Cage%20DESC"
```

## Pagination

The list endpoint returns a `ListResult<TSummary>` with three fields:

| Field             | Description                                                                    |
| ----------------- | ------------------------------------------------------------------------------ |
| `entities`        | The page of results                                                            |
| `total_size`      | Total count of matching resources across all pages                             |
| `next_page_token` | Token to pass as `page_token` to fetch the next page (null when no more pages) |

### Page size

```shell
# Get the first 2 students
curl "http://localhost:5000/students?page_size=2"
```

Response:

```json
{
  "students": [
    { "full_name": "Alice", "age": 20, "name": "1" },
    { "full_name": "Bob", "age": 22, "name": "2" }
  ],
  "total_size": 5,
  "next_page_token": "eyJza..."
}
```

### Next page

Pass the `next_page_token` from the previous response:

```shell
curl "http://localhost:5000/students?page_size=2&page_token=eyJza..."
```

Continue until `next_page_token` is `null`.

### Skip

`skip` offsets the result set before applying `page_size`:

```shell
# Skip the first 3 results, then return up to 10
curl "http://localhost:5000/students?skip=3&page_size=10"
```

## Show deleted

Soft-deleted students are excluded from list results by default. Pass `show_deleted=true` to include them:

```shell
curl "http://localhost:5000/students?show_deleted=true"
```

## Combining parameters

All parameters can be combined freely:

```shell
curl "http://localhost:5000/students?filter=age%20%3E%2018&order_by=age%20DESC&page_size=10&show_deleted=true"
```

## Verify

Seed a few students and try different combinations:

```shell
# Create several students
curl -X POST http://localhost:5000/students -H "Content-Type: application/json" -d '{"full_name":"Alice","age":20}'
curl -X POST http://localhost:5000/students -H "Content-Type: application/json" -d '{"full_name":"Bob","age":22}'
curl -X POST http://localhost:5000/students -H "Content-Type: application/json" -d '{"full_name":"Charlie","age":19}'
curl -X POST http://localhost:5000/students -H "Content-Type: application/json" -d '{"full_name":"Diana","age":25}'

# Filter: age >= 20, ordered by age descending, page size 2
curl "http://localhost:5000/students?filter=age%20%3E%3D%2020&order_by=age%20DESC&page_size=2"
```

Expected response:

```json
{
  "students": [
    { "full_name": "Diana", "age": 25, "name": "..." },
    { "full_name": "Bob", "age": 22, "name": "..." }
  ],
  "total_size": 3,
  "next_page_token": "eyJza..."
}
```

## Next steps

- [Query Caching](query-caching.md) — transparent query result caching with auto-eviction
- [Validation](validation.md) — add input validation with FluentValidation

## Further reading

- [Filtering](../documents/resource/filtering.md)
