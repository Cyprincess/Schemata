# Traits

A trait is a reusable group of fields. The generator emits each trait as a `public interface`
named `I{TraitName}`. Entities and other traits compose a trait through a `Use` declaration or a
base list.

## Syntax

```text
Trait <Name> [: <bases>] {
    [Note ...]
    [Use <trait-names>]
    [Fields]
}
```

A trait body holds notes, `Use` declarations, and fields. `Object` blocks, `Index` declarations,
and `Enum` declarations are entity-only.

## Composition

A base list is equivalent to a `Use` declaration. `EntityGenerator.GenerateUses` collects names
from both `Uses` and `Bases`, prefixes any name that resolves to a trait declared in the same
document with `I`, deduplicates, and writes them as the interface's base list.

```text
// Equivalent:
Trait Entity : Identifier, Timestamp { }

Trait Entity {
    Use Identifier, Timestamp
}
```

## Emission

`TraitGenerator` emits a `public interface I{Name}`. Each field becomes a
`public <CLRType> <PascalCaseName> { get; set; }` property. Base and `Use` names that resolve to
known traits appear as base interfaces.

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

## See also

- [Entities](entities.md) — how entities compose traits
- [Documents: Entity Traits](../documents/entity/traits.md) — the runtime trait interfaces a generated trait can stand in for
