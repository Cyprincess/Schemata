# Entities

The generator emits each entity as a `public record` with one init-set auto-property per field.
Trait interfaces from `Use` declarations and the base list appear in the record's base list.

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

Members may appear in any order.

## Members

| Member   | Emitted                          |
| -------- | -------------------------------- |
| `Note`   | No                               |
| `Use`    | Base list only                   |
| `Enum`   | Yes (inline, via `EnumGenerator`) |
| `Trait`  | No                               |
| `Object` | No (stored as a `View` node)     |
| `Index`  | No (stored as a `Pointer` node)  |
| Field    | Yes                              |

## Composition

Entities use the same `Use` and base-list syntax as traits. `EntityGenerator.GenerateUses`
collects names from `Uses` and `Bases`, resolves any name matching a declared trait to its
`I`-prefixed interface, and deduplicates.

```text
Entity Student : Entity, SoftDelete {
    // equivalent to: Use Entity, SoftDelete
}
```

## Emission

`EntityGenerator` emits a `public record` whose name matches the entity name. Each field becomes
a `public <CLRType> <PascalCaseName> { get; set; } = <default>;` property:

- Non-nullable `string` defaults to `string.Empty`.
- Nullable fields default to `null`.
- Every other field defaults to `default`.

Nested `Enum` declarations are emitted inline before the entity's fields.

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

```csharp
namespace My.Models {
    public record Student : IIdentifier {
        public System.Int64 Id { get; set; } = default;
        public System.String FullName { get; set; } = string.Empty;
        public System.Int32 Age { get; set; } = default;
    }
}
```

## Index and Object members

`Index col1 col2 [options]` is stored as a `Pointer` node and `Object name { ... }` as a `View`
node on the entity. Both record intent for tooling and produce no C# output.

```text
Entity Post {
    Use Entity

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
    Index user_id category_id [unique]

    Object response {
        id
        title
        body
        status
    }
}
```

## See also

- [Traits](traits.md) — trait composition and interface emission
- [Objects](objects.md) — `Object` block syntax
