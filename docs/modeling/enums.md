# Enums

Enums define named constant types. They can appear at the top level of a document or nested inside an entity body.

## Syntax

```
Enum <Name> {
    [<EnumValue> [,] ...]
    [Notes]
}

EnumValue = <Name> [= <value>] [{ <notes> }]
```

## Value Assignment

Enum values can be assigned explicitly or left for the compiler to assign sequentially:

```
// Explicit numeric values
Enum Priority {
    Low = 1
    Medium = 2
    High = 3
}

// Implicit values (compiler assigns sequentially)
Enum Color {
    Red
    Green
    Blue
}

// String values
Enum Format {
    Json = 'json'
    Xml = 'xml'
}

```

Supported value expression types:

- **Number**: `Draft = 1`
- **String**: `Red = 'red'`
- **Boolean**: `Active = true`
- **Null**: `Unknown = null`
- **Omitted**: compiler assigns sequentially

## Commas

Commas between enum values are optional:

```
// Both are valid:
Enum Color { Red, Green, Blue }
Enum Color { Red Green Blue }
```

## Notes on Enum Values

Each enum value can have an attached note block:

```
Enum Status {
    Draft { Note 'Not publicly accessible' }
    Published { Note 'Visible to all' }
    Archived
}
```

## Nested Enums

Enums can be declared inside an entity body:

```
Entity Post {
    Enum Status {
        Draft
        Published
    }

    Status status { Default 'Draft' }
}
```
