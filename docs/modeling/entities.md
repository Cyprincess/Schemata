# Entities

The generator emits each entity as a `public record` with auto-properties for its fields and
required property contracts composed through `Use`.

The generator emits no resource registration. To expose a generated entity as an AIP resource, register it
explicitly with `AddResource<TEntity>()` (or `Use<TEntity, TRequest, TDetail, TSummary>()`) on the resource
builder — see [Documents: Resource Overview](../documents/resource/overview.md).

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

| Member   | Emitted                           |
| -------- | --------------------------------- |
| `Note`   | No                                |
| `Use`    | Interface base list and required properties |
| `Enum`   | Yes (inline, via `EnumGenerator`) |
| `Trait`  | No                                |
| `Object` | No (stored as a `View` node)      |
| `Index`  | No (stored as a `Pointer` node)   |
| Field    | Yes                               |

## Composition

Entities use the same `Use` and base-list syntax as traits. `EntityGenerator.GenerateUses`
collects names from `Uses` and `Bases`, resolves any name matching a declared trait to its
`I`-prefixed interface, and deduplicates.

```text
Entity Student : Entity, SoftDelete {
    // equivalent to: Use Entity, SoftDelete
}
```

`Use` can name an accessible closed C# interface from the consuming compilation:

```text
Namespace My.Models
Entity AuditRow {
    Use Schemata.Abstractions.Entities.ITimestamp
}
```

This emits `System.DateTime? CreateTime` and `UpdateTime` properties. Interface property types,
nullable annotations and required get/set/init accessors come from Roslyn symbols. Inherited
properties are deduplicated; compatible explicit DSL fields are reused. Local trait fields are
also composed recursively. Default interface implementations remain on the interface.

Closed generic arguments support nullable, array and nested generic types. Required methods,
events, indexers, static-abstract members and property types that cannot be stored in a record
produce `SKM001`, as do unresolved, inaccessible, open or conflicting contracts. Invalid document
syntax produces `SKM002`. Diagnostics identify the `.skm` file and affected entity or member.
Property synthesis assigns no resource names and performs no timestamp updates.


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
