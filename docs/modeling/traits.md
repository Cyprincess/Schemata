# Traits

Traits are reusable field groups. The generator emits each trait as a `public interface` whose name is prefixed with `I`. Entities and other traits compose them via `Use` declarations or base lists.

## Syntax

```text
Trait <Name> [: <bases>] {
    [Note ...]
    [Use <trait-names>]
    [Fields]
}
```

## Allowed members

Trait bodies may contain three kinds of members, in any order:

| Member | Description                          |
| ------ | ------------------------------------ |
| `Note` | Documentation string (AST only)      |
| `Use`  | Incorporate fields from other traits |
| Field  | Property declaration                 |

Traits cannot contain `Object` blocks, `Index` declarations, or `Enum` declarations. Those are entity-only constructs.

## Composition with Use

`Use` resolves names against traits defined in the same document. If a name matches a known trait, the generator prefixes it with `I` in the base list of the emitted interface.

```text
Trait Timestamp {
    timestamp? create_time
    timestamp? update_time
}

Trait Auditable {
    Use Timestamp
    long? created_by_id
    long? updated_by_id
}
```

## Base lists

Base lists are semantically equivalent to `Use` declarations. Both are processed by `EntityGenerator.GenerateUses`, which collects all names from `Uses` and `Bases`, resolves trait names to their `I`-prefixed interface form, and deduplicates.

```text
// These are equivalent:
Trait Entity : Identifier, Timestamp { }

Trait Entity {
    Use Identifier, Timestamp
}
```

## What the generator emits

`TraitGenerator.Generate` emits a `public interface` for each trait:

- The interface name is `I` + the trait name.
- Each field becomes a `public <CLRType> <PascalCaseName> { get; set; }` property.
- Field options and field properties are **not consumed** — they are parsed but not emitted.
- `Note` members are **not emitted**.
- `Use` and base-list names that resolve to known traits appear as base interfaces on the emitted interface.

Given:

```text
Namespace My.Models

Trait Identifier {
    long id [primary key]
}

Trait Timestamp {
    timestamp? create_time
    timestamp? update_time
}
```

The generator produces:

```csharp
namespace My.Models {
    public interface IIdentifier {
        public System.Int64 Id { get; set; }
    }
}

namespace My.Models {
    public interface ITimestamp {
        public System.DateTimeOffset? CreateTime { get; set; }
        public System.DateTimeOffset? UpdateTime { get; set; }
    }
}
```

## Example

```text
Trait Identifier {
    Note 'Int64 primary key'
    long id [primary key]
}

Trait Timestamp {
    timestamp? create_time
    timestamp? update_time
}

Trait SoftDelete {
    timestamp? delete_time
    timestamp? purge_time
}

Trait Entity : Identifier, Timestamp {
    Note 'Combines identifier and timestamp'
}
```

## See also

- [Fields](fields.md) — field syntax and name conversion
- [Types](types.md) — built-in type token table
- [Entities](entities.md) — how entities compose traits
- [Grammar](grammar.md) — trait production rule
- [Documents: Entity Traits](../documents/entity/traits.md) — runtime trait interfaces the generator can implement
