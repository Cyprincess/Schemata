# Types

SKM has a set of built-in types that map to CLR types. Any type name not matching a built-in type is treated as a custom type reference, allowing references to other entities, enums, or external types.

## Built-in Types

All type names are case-insensitive.

| SKM Type     | Aliases                    | CLR Type                     |
| ------------ | -------------------------- | ---------------------------- |
| `string`     | `text`                     | `System.String`              |
| `int`        | `integer`, `int32`, `int4` | `System.Int32`               |
| `long`       | `int64`, `int8`            | `System.Int64`               |
| `float`      | `float32`, `float4`        | `System.Single`              |
| `double`     | `float64`, `float8`        | `System.Double`              |
| `decimal`    | `numeric`, `number`        | `System.Decimal`             |
| `boolean`    | `bool`                     | `System.Boolean`             |
| `datetime`   | `timestamp`                | `System.DateTimeOffset`      |
| `guid`       | `uuid`                     | `System.Guid`                |
| `biginteger` | `bigint`                   | `System.Numerics.BigInteger` |

Note that `datetime` and `timestamp` both map to `DateTimeOffset`, not `DateTime`.

## Nullable Types

Append `?` to any type to make it nullable:

```
string? author
timestamp? create_time
long? parent_id
```

## Custom Types

Any type name that does not match a built-in type is treated as a custom type reference:

```
BookStatus status
Category.response category
```

This allows referencing:

- Enums defined in the same document or namespace
- Other entities
- External types

## Type Resolution

Type names are resolved by checking against the built-in type table (case-insensitive match). If no match is found, the type name is treated as a custom type.
