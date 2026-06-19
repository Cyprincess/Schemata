# Fields

A field declares a type, a name, optional constraints in square brackets, and optional metadata
in curly braces. Fields appear in trait and entity bodies.

## Syntax

```text
<type>[?] <name> [<options>] [{ <notes and properties> }]
```

```text
long id [primary key]
string title [not null] { Length 500 }
string? author
timestamp? create_time
BookStatus status { Default 'Published' }
```

## Name conversion

Field names are written in `snake_case`. `Utilities.ToCamelCase` splits on `_` and spaces and
capitalizes each segment, producing the PascalCase C# property name.

| SKM name        | C# property    |
| --------------- | -------------- |
| `id`            | `Id`           |
| `full_name`     | `FullName`     |
| `create_time`   | `CreateTime`   |
| `email_address` | `EmailAddress` |

## Field options

Options are comma-separated inside square brackets and can span multiple lines. The parser joins
the words of each entry, lowercases them, and strips spaces and underscores before matching, so a
multi-word form and its concatenated form are interchangeable.

| Accepted forms                                | Field option                |
| --------------------------------------------- | --------------------------- |
| `primary key`, `PrimaryKey`                   | `FieldOption.PrimaryKey`    |
| `auto increment`, `AutoIncrement`             | `FieldOption.AutoIncrement` |
| `not null`, `NotNull`, `required`, `Required` | `FieldOption.Required`      |
| `unique`, `Unique`                            | `FieldOption.Unique`        |
| `b tree`, `BTree`                             | `FieldOption.BTree`         |
| `hash`, `Hash`                                | `FieldOption.Hash`          |

`not null` and `required` are aliases for `FieldOption.Required`. An unrecognized option is a
parse error.

```text
long id [primary key, auto increment]
string email [unique, not null]
long user_id [
    b tree
]
```

## Field properties

Properties supply column-level metadata as `Key Expression` pairs inside curly braces. The
property value is an [expression](expressions.md). `SkmConstants.Properties` names four keys;
any other key parses and is stored on the field.

| Property    | Meaning               |
| ----------- | --------------------- |
| `Default`   | Default column value  |
| `Length`    | Maximum string length |
| `Precision` | Decimal precision     |
| `Algorithm` | Hash algorithm name   |

```text
string title [not null] { Length 500 }
decimal price            { Precision 2 }
Status status            { Default 'Published' }
string hash              { Algorithm 'SHA256' }
```

## Notes

`Note` attaches a documentation string to a field's metadata block, and may also appear at the
top of a trait or entity body.

```text
Note 'Single-line note'
Note '''Multi-line note
that spans multiple lines'''
```

Field options, field properties, and notes are stored on the field's AST node. The entity and
trait generators emit only the type and name; they do not read options, properties, or notes.
