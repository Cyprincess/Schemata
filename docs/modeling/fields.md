# Fields

Fields define the properties of traits and entities. Each field declares a type, a name, optional constraints in square brackets, and optional metadata in curly braces.

## Syntax

```text
<type>[?] <name> [<options>] [{ <notes and properties> }]
```

Examples:

```text
long id [primary key]
string title [not null] { Length 500 }
string? author
timestamp? create_time
BookStatus status { Default 'Published' }
```

## Name conversion

Field names are written in `snake_case` in SKM. `Utilities.ToCamelCase` converts them to PascalCase for the emitted C# property by splitting on `_` and spaces, then capitalizing each segment:

| SKM name        | C# property    |
| --------------- | -------------- |
| `id`            | `Id`           |
| `full_name`     | `FullName`     |
| `create_time`   | `CreateTime`   |
| `email_address` | `EmailAddress` |

## Field options

Options are declared in square brackets, comma-separated. All options are case-insensitive. The parser normalizes by lowercasing and stripping spaces and underscores before matching, so multi-word forms and their CamelCase equivalents are interchangeable.

| Accepted forms                        | Normalized key  | Enum value                    |
| ------------------------------------- | --------------- | ----------------------------- |
| `primary key`, `PrimaryKey`           | `primarykey`    | `FieldOption.PrimaryKey`      |
| `auto increment`, `AutoIncrement`     | `autoincrement` | `FieldOption.AutoIncrement`   |
| `not null`, `NotNull`, `required`, `Required` | `notnull` / `required` | `FieldOption.Required` |
| `unique`, `Unique`                    | `unique`        | `FieldOption.Unique`          |
| `b tree`, `BTree`                     | `btree`         | `FieldOption.BTree`           |
| `hash`, `Hash`                        | `hash`          | `FieldOption.Hash`            |

`not null` and `required` both normalize to `FieldOption.Required` — they are aliases.

```text
long id [primary key]
string email [unique, not null]
long user_id [b tree]
long id [primary key, auto increment]
```

Options can span multiple lines:

```text
long user_id [
    b tree
]
```

**Parsed but not emitted.** `EntityGenerator` and `TraitGenerator` do not read field options when emitting C# properties. The options are stored in the AST and reserved for forward compatibility (e.g., future EF Core fluent-API generation).

## Field properties

Properties provide column-level metadata. They are declared inside curly braces after the options, as `Key Expression` pairs. Recognized keys are defined in `SkmConstants.Properties`:

| Property    | Value type | Description           |
| ----------- | ---------- | --------------------- |
| `Default`   | Expression | Default column value  |
| `Length`    | Expression | Maximum string length |
| `Precision` | Expression | Decimal precision     |
| `Algorithm` | Expression | Hash algorithm name   |

```text
string title [not null] { Length 500 }
decimal price            { Precision 2 }
Status status            { Default 'Published' }
string hash              { Algorithm 'SHA256' }
```

Arbitrary property keys are accepted syntactically. The four recognized keys above are stored in the AST alongside any others.

**Parsed but not emitted.** `EntityGenerator` and `TraitGenerator` do not consume field properties when emitting C# properties. They are reserved for forward compatibility.

## Notes

`Note` declarations attach documentation strings to a field's property block. They can also appear at the top level of trait and entity bodies.

```text
Note 'Single-line note'
Note '''Multi-line note
that spans multiple lines'''
```

Notes are stored in the AST but do not appear in the generated C# output.

## See also

- [Types](types.md) — built-in type token table
- [Grammar](grammar.md) — field production rule
- [Traits](traits.md) — trait body and generator emission
- [Entities](entities.md) — entity body and generator emission
- [Expressions](expressions.md) — expression forms used in property values
