# Types

SKM has a set of built-in type tokens that map directly to CLR types. The mapping is implemented in `Utilities.GetClrType` and the token constants live in `SkmConstants.Types`. Any type name that does not match a built-in token is passed through as-is, allowing references to other entities, enums, or external CLR types.

## Built-in type mappings

All type names are case-insensitive. The generator uses `clr.FullName` when emitting the C# property type, so the output is always the fully-qualified CLR name.

| SKM token    | Aliases                          | C# type                      |
| ------------ | -------------------------------- | ---------------------------- |
| `string`     | `text`                           | `string`                     |
| `int`        | `integer`, `int32`, `int4`       | `int`                        |
| `long`       | `int64`, `int8`                  | `long`                       |
| `biginteger` | `bigint`                         | `System.Numerics.BigInteger` |
| `float`      | `float32`, `float4`              | `float`                      |
| `double`     | `float64`, `float8`              | `double`                     |
| `decimal`    | `numeric`, `number`              | `decimal`                    |
| `boolean`    | `bool`                           | `bool`                       |
| `datetime`   | `timestamp`                      | `DateTimeOffset`             |
| `guid`       | `uuid`                           | `Guid`                       |

Quick reference (token -> C# type):

```text
string -> string        text -> string
int -> int              integer / int32 / int4 -> int
long -> long            int64 / int8 -> long
biginteger -> System.Numerics.BigInteger    bigint -> System.Numerics.BigInteger
float -> float          float32 / float4 -> float
double -> double        float64 / float8 -> double
decimal -> decimal      numeric / number -> decimal
boolean -> bool         bool -> bool
datetime -> DateTimeOffset    timestamp -> DateTimeOffset
guid -> Guid            uuid -> Guid
```

Note that `datetime` and `timestamp` both map to `DateTimeOffset`, not `DateTime`.

## Nullable types

Append `?` to any type to make it nullable. The generator appends `?` to the CLR type name and sets the default value to `null`:

```text
string? author
timestamp? create_time
long? parent_id
```

For non-nullable `string`, the generator emits `= string.Empty` as the default. For all other non-nullable built-in types it emits `= default`.

## Custom types

Any type name that does not match a built-in token is treated as a custom type reference and written verbatim into the generated C#:

```text
BookStatus status
Category.Response category
```

This allows referencing enums defined in the same document, other entities, or external CLR types. The generator does not validate that the referenced type exists.

## See also

- [Fields](fields.md) — how types appear in field declarations
- [Grammar](grammar.md) — type specifier production rule
- [Entities](entities.md) — entity record emission
- [Traits](traits.md) — trait interface emission
