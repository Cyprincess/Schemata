# Expressions

Expressions appear in field property values, enum value assignments, and Object field computed values.

## Expression Types

| Type            | Syntax                       | Example              |
| --------------- | ---------------------------- | -------------------- |
| String literal  | `'value'` or `"value"`       | `Default 'Draft'`    |
| Number literal  | Digits with optional decimal | `Length 500`         |
| Boolean literal | `true` or `false`            | `Default: true`      |
| Null literal    | `null`                       | `Default: null`      |
| Function call   | `name(args)`                 | `= obfuscate(email)` |
| Reference       | Qualified name               | `= category.id`      |

## Disambiguation

When parsing an expression, the parser tests in this order:

1. **Literals** — string, boolean, `null`, number
2. **Function call** — a qualified name followed by `(`
3. **Reference** — any other qualified name

## Literals

### Strings

Four string formats are supported:

```
'single-quoted string'
"double-quoted string"
'''triple single-quoted
multi-line string'''
"""triple double-quoted
multi-line string"""
```

### Numbers

Numbers support an optional decimal point:

```
42
3.14
0.5
```

### Boolean and Null

```
true
false
null
```

## Function Calls

Function calls specify a function name and zero or more argument expressions:

```
= obfuscate(email_address)
= now()
= format(first_name, last_name)
```

## References

References are dot-separated qualified names that resolve to another field or type:

```
= category.id
= Status.Draft
= parent.name
```

In Object field assignments, references generate property access in the DTO constructor or factory method.

## Usage Contexts

| Context                 | Allowed Expressions | Example               |
| ----------------------- | ------------------- | --------------------- |
| Field property value    | All                 | `{ Default 'Draft' }` |
| Enum value assignment   | Literal             | `Draft = 1`           |
| Object field assignment | All                 | `= obfuscate(email)`  |
