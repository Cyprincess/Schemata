# Objects

An `Object` block declares a projection over an entity. The parser stores it as a `View` node on
the enclosing entity; the generator produces no C# from it. To define request, detail, or summary
DTOs for a resource, write conventional C# types — see
[Documents: Resource Overview](../documents/resource/overview.md).

## Syntax

```text
Object <name> {
    [Note ...]
    [ViewFields...]
}
```

The keyword is `Object` (case-insensitive).

## ViewField forms

A ViewField takes one of three forms. The parser distinguishes them by peeking at the token after
the second identifier: when it is `[`, `{`, or `=`, the first qualified name is the type and the
second identifier is the field name; otherwise the qualified name is the field name with no type.

**Untyped** — field name only; the type is inherited from the parent entity:

```text
id
full_name
create_time
```

**Nullable typed** — type with `?`, then field name:

```text
string? display_name
timestamp? deleted_at
```

**Typed** — type then field name, followed by `[`, `{`, or `=`:

```text
string title [omit]
int age { Note 'years' }
string label = format(first_name, last_name)
```

## Options

```text
[omit]      -- mark the field as omittable
[omit all]  -- embedded projection: include only the listed subfields
```

## Nested and computed fields

ViewFields nest inside `{ }`, and a field may carry an `= expression` assignment.

```text
Object response {
    User.response author [omit all] {
        id
        nickname
    }
    title
    obfuscated_email = obfuscate(email_address)
    category_id      = category.id
}
```

## See also

- [Expressions](expressions.md) — forms accepted in a ViewField assignment
- [Grammar](grammar.md) — view and view-field production rules
