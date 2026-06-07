# Enums

Enums define named constant types. They can appear at the top level of a document or nested inside an entity body. The generator emits each enum as a `public enum`.

## Syntax

```text
Enum <Name> {
    [Note ...]
    [<EnumValue> [,] ...]
}

EnumValue = <Name> [= <literal>] [{ <notes> }]
```

## Value assignment

Enum value assignments accept only **literals** — the parser uses `Literal` (not the full `Expression`) for the right-hand side of `=`. Function calls and references are not valid here.

```text
// Numeric values
Enum Priority {
    Low    = 1
    Medium = 2
    High   = 3
}

// String values
Enum Format {
    Json = 'json'
    Xml  = 'xml'
}

// Implicit values (no assignment)
Enum Color {
    Red
    Green
    Blue
}
```

## What the generator emits

`EnumGenerator.GenerateValues` handles three literal cases:

| Assignment type | Example SKM         | Emitted C#                  |
| --------------- | ------------------- | --------------------------- |
| `NumberLiteral` | `Draft = 0`         | `Draft = 0,`                |
| `Literal` (string) | `Json = 'json'`  | `Json = "json",`            |
| `Reference`     | `Alias = Other.Val` | `Alias = Other.Val,`        |
| No assignment   | `Red`               | `Red,`                      |

Any other literal type (boolean, null) falls through to `value.Assignment.ToString()`, which produces the raw string representation.

Given:

```text
Namespace My.Models

Enum BookStatus {
    BookStatusUnspecified = 0
    Draft                 = 1
    Published             = 2
    Archived              = 3
}
```

The generator produces:

```csharp
namespace My.Models {
    public enum BookStatus {
        BookStatusUnspecified = 0,
        Draft = 1,
        Published = 2,
        Archived = 3,
    }
}
```

## Commas

Commas between enum values are optional. The parser accepts `[","]` before each `note | enum value` entry:

```text
// Both are valid:
Enum Color { Red, Green, Blue }
Enum Color { Red Green Blue }
```

## Notes on enum values

Each enum value can have an attached note block. Notes are stored in the AST but not emitted:

```text
Enum Status {
    Draft     { Note 'Not publicly accessible' }
    Published { Note 'Visible to all' }
    Archived
}
```

## Nested enums

Enums can be declared inside an entity body. `EntityGenerator` calls `EnumGenerator` for each nested enum before emitting the entity's fields. Nested enums are emitted without a namespace wrapper when called from `EntityGenerator` (the `doc` parameter is `null`):

```text
Entity Post {
    Enum Status {
        Draft
        Published
    }

    Status status { Default 'Draft' }
}
```

## See also

- [Entities](entities.md) — nested enum emission
- [Expressions](expressions.md) — literal forms accepted in value assignments
- [Grammar](grammar.md) — enumeration production rule
