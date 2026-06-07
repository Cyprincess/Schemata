# Objects

Object blocks (`Object Name { ... }`) are parsed by the grammar as `View` AST nodes and stored on the enclosing entity. **The generator does not emit DTOs from Object blocks today.** The AST is reserved for future projection codegen.

Do not rely on Object blocks producing any C# output. If you need request/detail/summary DTOs for a resource, define them as conventional C# classes or records. See [Documents: Resource Overview](../documents/resource/overview.md) for the `TRequest`, `TDetail`, and `TSummary` type parameters.

## Parser-accepted syntax

The parser accepts Object blocks inside entity bodies. The keyword is `Object` (case-insensitive).

```text
Object <name> {
    [Note ...]
    [ViewFields...]
}
```

### ViewField forms

A ViewField inside an Object block can take three forms:

**Untyped** — field name only; type is inferred from the parent entity:

```text
id
full_name
create_time
```

**Typed with nullable marker** — type specifier with `?`, then field name:

```text
string? display_name
timestamp? deleted_at
```

**Typed with continuation** — type specifier followed by field name, where the next token is `[`, `{`, or `=`:

```text
string title [omit]
int age { Note 'years' }
string label = format(first_name, last_name)
```

The parser disambiguates typed from untyped by peeking at the token after the second identifier: if it is `[`, `{`, or `=`, the first qualified name is the type and the second identifier is the field name. Otherwise the qualified name is the field name with no explicit type.

### ViewField options

```text
[omit]      -- marks the field as omittable
[omit all]  -- embedded projection; include only explicitly listed subfields
```

### Nested ViewFields

ViewFields can nest recursively inside `{ }` blocks:

```text
Object response {
    User.response author [omit all] {
        id
        nickname
    }
    title
    body
}
```

### Computed fields

A ViewField can carry an `= expression` assignment:

```text
Object response {
    id
    obfuscated_email = obfuscate(email_address)
    category_id      = category.id
}
```

See [Expressions](expressions.md) for the expression forms the parser accepts.

## Full example (parsed, not emitted)

```text
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

The parser stores `detail` and `summary` as `View` nodes on the `Student` entity's AST. No C# is generated from them.

## See also

- [Entities](entities.md) — entity body members and what is emitted
- [Expressions](expressions.md) — expression forms used in ViewField assignments
- [Grammar](grammar.md) — view and view field production rules
- [Documents: Resource Overview](../documents/resource/overview.md) — conventional C# DTO types for resources
