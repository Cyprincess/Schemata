# Expressions

Expressions appear in field property values (`{ Default 'Draft' }`), `Object` ViewField
assignments (`= category.id`), and enum value assignments (`Draft = 1`). An expression is a
literal, a function call, or a reference. There are no operators or precedence.

```text
expression = literal | function call | reference
```

The parser tries literal first, then function call (a qualified name followed by `(`), then
reference.

## Literals

Strings come in four forms; the triple-quoted forms are tried before the standard quoted forms.
Standard quoting delegates to Parlot's `Terms.String()` and numbers to `Terms.Decimal()`.

```text
'single-quoted'
"double-quoted"
'''triple single-quoted
multi-line'''
"""triple double-quoted
multi-line"""

42      3.14      0.5
true    false     null
```

`true`, `false`, and `null` are matched case-insensitively.

## Function calls

A function call is a qualified name, `(`, zero or more comma-separated argument expressions, and
`)`. Arguments may be any expression form, including nested calls.

```text
= now()
= obfuscate(email_address)
= format(first_name, last_name)
= coalesce(display_name, full_name)
```

## References

A reference is a dot-separated qualified name that is neither a literal nor followed by `(`.

```text
= category.id
= Status.Draft
= parent.name
```

## Where each form is accepted

| Context                | Accepted forms |
| ---------------------- | -------------- |
| Field property value   | All            |
| `Object` ViewField `=` | All            |
| Enum value assignment  | Literal only   |

Enum value assignments use the `Literal` sub-parser, so a function call or reference after `=` in
an enum value is a parse error.
