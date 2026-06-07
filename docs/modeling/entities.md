# Entities

Entities are the primary declaration type in SKM. The generator emits each entity as a `public record` with auto-properties for every declared field. Trait interfaces from `Use` declarations and base lists appear in the record's base list.

## Syntax

```text
Entity <Name> [: <bases>] {
    [Note ...]
    [Use <trait-names>]
    [Enum declarations]
    [Trait declarations]
    [Object blocks]
    [Index declarations]
    [Field declarations]
}
```

Members may appear in any order and can be freely intermixed.

## Allowed members

| Member          | Description                                          | Emitted? |
| --------------- | ---------------------------------------------------- | -------- |
| `Note`          | Documentation string                                 | No       |
| `Use`           | Incorporate trait interfaces into the base list      | Yes (base list only) |
| `Enum`          | Nested enum declaration                              | Yes      |
| `Trait`         | Nested trait declaration                             | No       |
| `Object`        | DTO projection block (parsed as `View` AST node)     | No       |
| `Index`         | Index declaration (parsed as `Pointer` AST node)     | No       |
| Field           | Property declaration                                 | Yes      |

Nested `Trait` declarations, `Object` blocks, and `Index` declarations are parsed and stored in the AST but **not emitted as C# today**. See [Objects](objects.md) for the Object block story.

## Composition

Entities use the same `Use` and base-list syntax as traits. `EntityGenerator.GenerateUses` collects names from both `Uses` and `Bases`, resolves any name that matches a known trait to its `I`-prefixed interface form, deduplicates, and writes them as the record's base list.

```text
Entity Student : Entity, SoftDelete {
    // equivalent to: Use Entity, SoftDelete
}
```

## What the generator emits

`EntityGenerator.Generate` emits a `public record` for each entity:

- The record name matches the entity name exactly.
- Each field becomes a `public <CLRType> <PascalCaseName> { get; set; } = <default>;` property.
- Non-nullable `string` fields default to `string.Empty`. All other non-nullable fields default to `default`. Nullable fields default to `null`.
- Field options and field properties are **not consumed** — parsed but not emitted.
- `Note` members are **not emitted**.
- Nested `Trait`, `Object`, and `Index` members are **not emitted**.
- Nested `Enum` declarations are emitted inline via `EnumGenerator`.

Given:

```text
Namespace My.Models

Trait Identifier {
    long id [primary key]
}

Entity Student : Identifier {
    string full_name [not null]
    int age
}
```

The generator produces:

```csharp
namespace My.Models {
    public record Student : IIdentifier {
        public System.Int64 Id { get; set; } = default;
        public System.String FullName { get; set; } = string.Empty;
        public System.Int32 Age { get; set; } = default;
    }
}
```

## Nested enums

Enums declared inside an entity body are emitted by `EnumGenerator` and appear before the entity's own fields in the generated source:

```text
Entity Post {
    Enum Status {
        Draft
        Published
        Archived
    }

    Status status { Default 'Draft' }
    string title [not null]
}
```

## Index declarations

Index declarations (`Index col1 col2 [options]`) are parsed as `Pointer` AST nodes and stored on the entity. They are **not emitted as C# today** and are reserved for future EF Core fluent-API generation.

```text
Entity Post {
    long user_id [b tree]
    long category_id

    Index user_id [b tree]
    Index user_id category_id [unique]
}
```

## Object blocks

Object blocks (`Object name { ... }`) are parsed as `View` AST nodes and stored on the entity. They are **not emitted as C# today**. See [Objects](objects.md).

## Complete example

```text
Entity Post {
    Use Entity, SoftDelete

    Enum Status {
        Draft
        Published
        Archived
    }

    long user_id [b tree]
    long category_id
    Status status { Default 'Draft' }
    string title [not null] { Length 500 }
    text body

    Index user_id [b tree]
    Index category_id [b tree]

    Object request {
        title
        body
        status
    }

    Object response {
        id
        title
        body
        status
        create_time
        update_time
    }
}
```

## See also

- [Fields](fields.md) — field syntax and name conversion
- [Types](types.md) — built-in type token table
- [Traits](traits.md) — trait body and interface emission
- [Enums](enums.md) — enum syntax and emission rules
- [Objects](objects.md) — Object block syntax (parsed, not emitted today)
- [Grammar](grammar.md) — entity production rule
