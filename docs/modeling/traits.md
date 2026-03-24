# Traits

Traits are reusable field groups. They provide a mechanism for composing common field sets across multiple entities.

## Syntax

```
Trait <Name> [: <bases>] {
    [Use <trait-names>]
    [Notes]
    [Fields]
}
```

## Composition with Use

Traits can incorporate fields from other traits using `Use` declarations:

```
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

`Use` resolves trait names against traits defined in the same document.

## Base Lists

Base lists are semantically equivalent to `Use` declarations:

```
// These are equivalent:
Trait Entity : Identifier, Timestamp { }

Trait Entity {
    Use Identifier, Timestamp
}
```

## Allowed Members

Trait bodies may contain:

| Member | Description                          |
| ------ | ------------------------------------ |
| `Note` | Documentation string                 |
| `Use`  | Incorporate fields from other traits |
| Field  | Property declaration                 |

Traits cannot contain `Object` blocks, `Index` declarations, or `Enum` declarations. Those are entity-only constructs.

## Example

```
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
