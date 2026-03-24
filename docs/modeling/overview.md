# Overview

The Schemata Modeling Language (SKM) is a domain-specific language for defining entities, traits, enums, and DTO projections in `.skm` files.

## Key Characteristics

- **Declarative syntax** focused on data schema definition
- **Case-insensitive keywords** (`Entity`, `entity`, and `ENTITY` are equivalent)
- **C-style comments** (`//` line comments and `/* */` block comments)
- **Snake_case field names** by convention

## Document Structure

An SKM file (`.skm`) is a document consisting of an optional namespace declaration followed by any number of top-level declarations:

```
Namespace My.Project.Models

Trait Identifier {
    long id [primary key]
}

Entity Student : Identifier {
    string full_name [not null]
    int age
}

Enum EnrollmentStatus {
    Draft
    Enrolled
    Graduated
}
```

The `Namespace` declaration, if present, must be the first non-comment element in the file.

## Top-Level Declarations

| Declaration | Description                                                  | Reference               |
| ----------- | ------------------------------------------------------------ | ----------------------- |
| `Trait`     | Reusable field groups                                        | [Traits](traits.md)     |
| `Entity`    | Entity definitions with fields, indexes, and nested elements | [Entities](entities.md) |
| `Enum`      | Named constant types                                         | [Enums](enums.md)       |

Entities may also contain nested declarations: enums, traits, fields, indexes, and object blocks (DTO projections).

## Language Reference

| Topic                         | Description                                          |
| ----------------------------- | ---------------------------------------------------- |
| [Types](types.md)             | Built-in type mappings, nullable types, custom types |
| [Fields](fields.md)           | Field syntax, options, properties, notes             |
| [Traits](traits.md)           | Reusable field groups                                |
| [Entities](entities.md)       | Entity definitions with nested elements and indexes  |
| [Enums](enums.md)             | Named constant types                                 |
| [Objects](objects.md)         | DTO projection blocks                                |
| [Expressions](expressions.md) | Literals, function calls, computed fields            |
| [Grammar](grammar.md)         | Complete EBNF reference                              |
