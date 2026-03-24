# Entities

Entities are the primary declaration type in SKM.

## Syntax

```
Entity <Name> [: <bases>] {
    [Use <trait-names>]
    [Enum declarations]
    [Trait declarations]
    [Field declarations]
    [Index declarations]
    [Object blocks]
}
```

Members may appear in any order and can be freely intermixed.

## Composition

Entities use the same `Use` and base-list syntax as traits:

```
Entity Student : Entity, SoftDelete {
    // equivalent to: Use Entity, SoftDelete
}
```

Base lists and `Use` declarations are semantically equivalent. See [Traits](traits.md) for details.

## Nested Enums

Enums can be declared inside an entity body:

```
Entity Post {
    Enum Status {
        Draft
        Published
        Archived
    }

    Status status { Default 'Draft' }
}
```

See [Enums](enums.md) for the full enum syntax.

## Nested Traits

Traits can also be declared inside an entity body. They follow the same syntax as top-level traits.

## Index Declarations

Index declarations define database indexes on one or more columns:

```
Index <column> [<additional-columns>...] [<options>] [{ <notes> }]
```

The index name is derived automatically: `IX_{Entity}_{Col1}_{Col2}_...`

**Index options:**

| Option   | Aliases  | Description             |
| -------- | -------- | ----------------------- |
| `unique` | `Unique` | Unique index constraint |
| `b tree` | `BTree`  | B-tree index            |
| `hash`   | `Hash`   | Hash index              |

Examples:

```
Entity Post {
    long user_id [b tree]
    long category_id
    string title [not null]

    Index user_id [b tree]
    Index user_id category_id [unique]
    Index title [b tree] {
        Note 'Full-text search index'
    }
}
```

## Object Blocks

Object blocks declare DTO projections of the entity. See [Objects](objects.md) for the complete syntax.

```
Entity Student {
    Use Entity
    string full_name
    int age

    Object detail {
        id
        full_name
        age
        create_time
        update_time
    }

    Object summary {
        id
        full_name
    }
}
```

## Complete Example

```
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
