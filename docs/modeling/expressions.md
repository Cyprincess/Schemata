# Expressions

Expressions appear in field property values (`{ Default 'Draft' }`), Object block ViewField assignments (`= category.id`), and enum value assignments (`Draft = 1`). The expression grammar has three forms: literal, function call, and reference. There are no operators and no precedence rules.

## Expression forms

```text
expression = literal | function call | reference
```

The parser tests in this order:

1. **Literal** — string, boolean, `null`, or number
2. **Function call** — a qualified name followed by `(`
3. **Reference** — any other qualified name

## Literals

### Strings

Four string forms are supported. Triple-quoted forms are tried before standard quoted forms:

```text
'single-quoted string'
"double-quoted string"
'''triple single-quoted
multi-line string'''
"""triple double-quoted
multi-line string"""
```

Standard single- and double-quoted strings delegate to Parlot's `Terms.String()`.

### Numbers

Number parsing delegates to Parlot's `Terms.Decimal()`:

```text
42
3.14
0.5
```

### Boolean

```text
true
false
```

Boolean keywords are matched case-insensitively.

### Null

```text
null
```

`null` is matched case-insensitively.

## Function calls

A function call is a qualified name followed by `(`, zero or more comma-separated expressions, and `)`. Arguments can be any expression form, including nested function calls:

```text
= obfuscate(email_address)
= now()
= format(first_name, last_name)
= coalesce(display_name, full_name)
```

## References

A reference is a dot-separated qualified name that does not match a literal and is not followed by `(`:

```text
= category.id
= Status.Draft
= parent.name
```

## Usage contexts

| Context                  | Accepted forms          | Notes                                      |
| ------------------------ | ----------------------- | ------------------------------------------ |
| Field property value     | All                     | `{ Default 'Draft' }`, `{ Length 500 }`    |
| Enum value assignment    | Literal only            | Parser uses `Literal`, not full expression |
| Object ViewField `=`     | All                     | `= obfuscate(email)`, `= category.id`      |

Enum value assignments accept only literals. The parser uses the `Literal` sub-parser (not the full `Expression` parser) for the right-hand side of `=` in enum values. Function calls and references are not valid there.

## See also

- [Fields](fields.md) — field property syntax
- [Enums](enums.md) — literal-only constraint on enum value assignments
- [Objects](objects.md) — ViewField assignment expressions
- [Grammar](grammar.md) — expression and literal production rules
