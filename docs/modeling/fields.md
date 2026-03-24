# Fields

Fields define the properties of traits and entities. Each field declares a type, a name, optional constraints, and optional metadata.

## Syntax

```
<type>[?] <name> [<options>] [{ <notes and properties> }]
```

Examples:

```
long id [primary key]
string title [not null] { Length 500 }
string? author
timestamp? create_time
BookStatus status { Default 'Published' }
```

## Name Conversion

Field names are written in `snake_case` in SKM. The generator converts them to PascalCase for the C# property name by splitting on `_` and spaces, then capitalizing each segment:

| SKM Name        | C# Property    |
| --------------- | -------------- |
| `id`            | `Id`           |
| `full_name`     | `FullName`     |
| `create_time`   | `CreateTime`   |
| `email_address` | `EmailAddress` |

## Field Options

Options are declared in square brackets, comma-separated. All options are case-insensitive, and multi-word forms are interchangeable with their concatenated CamelCase equivalents.

| Option                  | Aliases         | Description                   |
| ----------------------- | --------------- | ----------------------------- |
| `primary key`           | `PrimaryKey`    | Primary key constraint        |
| `auto increment`        | `AutoIncrement` | Auto-incrementing primary key |
| `not null` / `required` | `NotNull`       | Non-nullable constraint       |
| `unique`                | `Unique`        | Unique index                  |
| `b tree`                | `BTree`         | B-tree index                  |
| `hash`                  | `Hash`          | Hash index                    |

Examples:

```
long id [primary key]
string email [unique, not null]
long user_id [b tree]
long id [primary key, auto increment]
```

Options can span multiple lines:

```
long user_id [
    b tree
]
```

## Field Properties

Properties provide column-level metadata. They are declared inside curly braces after the options.

| Property    | Value Type | Description           |
| ----------- | ---------- | --------------------- |
| `Length`    | Number     | Maximum string length |
| `Precision` | Number     | Decimal precision     |
| `Default`   | Expression | Default column value  |
| `Algorithm` | String     | Hash algorithm name   |

Property values can be any expression type: string literals (`'value'`), numbers (`123`), booleans (`true`/`false`), `null`, references (`Status.Draft`), or function calls (`now()`).

```
string title [not null] { Length 500 }
decimal price { Precision 2 }
Status status { Default 'Published' }
string hash { Algorithm 'SHA256' }
```

Arbitrary property keys are accepted syntactically. The four recognized keys above have special meaning in code generation; all others are stored in the AST but do not affect the generated output.

## Notes

`Note` declarations attach documentation strings to any element. They can appear inside field property blocks, traits, entities, enums, and object blocks.

```
Note 'Single-line note'
Note '''Multi-line note
that spans multiple lines'''
```

String formats:

- Single-quoted: `'single line'`
- Double-quoted: `"single line"`
- Triple single-quoted: `'''multi-line'''`
- Triple double-quoted: `"""multi-line"""`

Notes are stored in the AST but do not appear in the generated C# output.
