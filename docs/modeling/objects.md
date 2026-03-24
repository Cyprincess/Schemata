# Objects

Object blocks declare DTO projections of an entity. They define a named subset of an entity's fields, optionally with type transformations and computed fields.

## Syntax

```
Object <name> {
    [Notes]
    [ViewFields...]
}
```

## ViewField Syntax

```
[<type>[?]] [<field-name>] [<view-options>] [{ <notes-and-viewfields> }] [= <expression>]
```

ViewFields support several patterns:

| Pattern             | Example                                           | Description                      |
| ------------------- | ------------------------------------------------- | -------------------------------- |
| Untyped field       | `id`                                              | Type inferred from parent entity |
| Typed field         | `string title`                                    | Explicit type                    |
| Optional field      | `email [omit]`                                    | Marks field as omittable         |
| Embedded projection | `User.response author [omit all] { id nickname }` | Includes only listed subfields   |
| Computed field      | `display = format(name)`                          | Value from expression            |

## Type Inference

When a ViewField does not specify an explicit type, the type is inferred in this priority order:

1. **Explicit type** -- if a type specifier is present, use it
2. **Expression type** -- if `= expression` is present, the return type of the expression
3. **Inherited type** -- inferred from the same-named field in the parent entity

## Basic Example

```
Entity Student {
    Use Entity
    string full_name [not null]
    string? email_address
    int age

    Object detail {
        id
        full_name
        email_address
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

## The `[omit]` Annotation

Fields marked with `[omit]` are intended for exclusion from the base variant.

```
Object response {
    id
    nickname
    email_address [omit]
    phone_number [omit]
}
```

## The `[omit all]` Annotation

`[omit all]` declares an embedded projection that includes only the explicitly listed subfields from a referenced Object type:

```
Object response {
    User.response author [omit all] {
        id
        nickname
    }
    title
    body
}
```

Nested ViewFields inside `[omit all]` blocks can have their own assignments:

```
Object response {
    Category.response category [omit all] {
        id = category_id
    }
}
```

## Computed Fields

Fields with `= expression` derive their value from an expression:

```
Object response {
    id
    nickname
    obfuscated_email [omit] = obfuscate(email_address)
    category_id [omit] = category.id
}
```

See [Expressions](expressions.md) for the full expression syntax.
