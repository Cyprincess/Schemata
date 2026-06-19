# Types

A field's type specifier is a type name with an optional `?`. Built-in type tokens map to CLR
types through `Utilities.GetClrType`; any name that does not match a token is written into the
generated C# verbatim. Type names are case-insensitive.

## Built-in type mappings

The generator emits `clr.FullName`, so the property type is always the fully-qualified CLR name.

| SKM token    | Aliases                     | C# type                      |
| ------------ | --------------------------- | ---------------------------- |
| `string`     | `text`                      | `string`                     |
| `int`        | `integer`, `int32`, `int4`  | `int`                        |
| `long`       | `int64`, `int8`             | `long`                       |
| `biginteger` | `bigint`                    | `System.Numerics.BigInteger` |
| `float`      | `float32`, `float4`         | `float`                      |
| `double`     | `float64`, `float8`         | `double`                     |
| `decimal`    | `numeric`, `number`         | `decimal`                    |
| `boolean`    | `bool`                      | `bool`                       |
| `datetime`   | `timestamp`                 | `DateTimeOffset`             |
| `guid`       | `uuid`                      | `Guid`                       |

The integer and float aliases follow byte-width naming: `int4`/`int8` are 4- and 8-byte
integers (`int`/`long`), `float4`/`float8` are 4- and 8-byte floats (`float`/`double`).
`datetime` and `timestamp` both map to `DateTimeOffset`, not `DateTime`.

## Nullable types

Append `?` to any type to make it nullable. The generator appends `?` to the CLR type name; in
entity records a nullable field defaults to `null`.

```text
string? author
timestamp? create_time
long? parent_id
```

## Custom types

Any name that does not match a built-in token is written into the generated C# as-is, which lets
a field reference an enum in the same document, another entity, or an external CLR type. The
generator does not check that the referenced type exists.

```text
BookStatus status
Category.Response category
```

## See also

- [Fields](fields.md) — how a type specifier appears in a field declaration
- [Entities](entities.md) — entity record emission and default values
