# Enums

An enum defines a named constant type. It appears at the top level of a document or nested inside
an entity body. The generator emits each enum as a `public enum`.

## Syntax

```text
Enum <Name> {
    [Note ...]
    [<EnumValue> [,] ...]
}

EnumValue = <Name> [= <integer literal>] [{ <notes> }]
```

The right-hand side of `=` is a decimal integer literal within `Int32` range. String, boolean,
`null`, fractional, and out-of-range assignments are rejected with the `SKM003` diagnostic and the
enum is skipped from generation. Commas between values are optional.

```text
Enum Priority {
    Low    = 1
    Medium = 2
    High   = 3
}

Enum Color { Red Green Blue }
```

## Emission

`EnumGenerator` renders each value by assignment kind:

| Assignment     | SKM                 | Emitted C#           |
| -------------- | ------------------- | -------------------- |
| number literal | `Draft = 0`         | `Draft = 0,`         |
| none           | `Red`               | `Red,`               |

A value with no assignment takes the next sequential integer from the C# compiler.

```text
Namespace My.Models

Enum BookStatus {
    BookStatusUnspecified = 0
    Draft                 = 1
    Published             = 2
    Archived              = 3
}
```

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

## Nested enums

An enum declared inside an entity body is emitted inline before the entity's fields, without a
namespace wrapper.

```text
Entity Post {
    Enum Status {
        Draft
        Published
    }

    Status status { Default 'Draft' }
}
```
