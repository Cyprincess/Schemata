# Grammar

The complete grammar for the Schemata Modeling Language (SKM), derived from `Parser.cs`. Whitespace (spaces, tabs, newlines) and comments are freely permitted between any two tokens and are not shown in the production rules. At least one whitespace character is required between adjacent identifier or keyword tokens. Keywords are matched case-insensitively.

## Document structure

```ebnf
document = [ namespace ], { declaration } ;

declaration = entity
            | trait
            | enumeration ;
```

The `Namespace` declaration, if present, must be the first non-comment element in the file. Only `Entity`, `Trait`, and `Enum` are top-level declarations. `Object` blocks and `Index` declarations are nested inside entity bodies; they are not top-level constructs.

## Declarations

```ebnf
namespace = "Namespace", qualified name ;

entity = "Entity", identifier,
         [ ":", name list ],
         "{", { entity member }, "}" ;

entity member = note | use | enumeration | trait
              | view | pointer | field ;

trait = "Trait", identifier,
        [ ":", name list ],
        "{", { trait member }, "}" ;

trait member = note | use | field ;

enumeration = "Enum", identifier,
              "{", { [ "," ], ( note | enum value ) }, "}" ;

enum value = identifier,
             [ "=", literal ],
             [ "{", { note }, "}" ] ;
```

Base lists and `Use` declarations are semantically equivalent. `Entity Foo : Bar, Baz` is sugar for `Use Bar, Baz` at the top of the body. Both resolve through the same field-incorporation mechanism in `EntityGenerator`.

## Field

```ebnf
field = type specifier, identifier,
        [ field options ],
        [ "{", { note | property }, "}" ] ;

type specifier = qualified name, [ "?" ] ;
```

Field names are written in `snake_case` and converted to PascalCase by the generator (`Utilities.ToCamelCase`).

## View (Object block)

Object blocks are parsed as `View` AST nodes. They are **not emitted as C# types today**. See [Objects](objects.md).

```ebnf
view = "Object", identifier,
       "{", { note | view field }, "}" ;

view field = nullable view field
           | typed view field
           | untyped view field ;

nullable view field = qualified name, "?", identifier,
                      [ view options ],
                      [ "{", { note | view field }, "}" ],
                      [ "=", expression ] ;

typed view field = qualified name, identifier,
                   ( view options | "{" | "=" ),
                   [ view options ],
                   [ "{", { note | view field }, "}" ],
                   [ "=", expression ] ;

untyped view field = qualified name,
                     [ view options ],
                     [ "{", { note | view field }, "}" ],
                     [ "=", expression ] ;
```

The parser disambiguates typed from untyped by peeking at the token after the second identifier: if it is `[`, `{`, or `=`, the first qualified name is the type and the second identifier is the field name. Otherwise the qualified name is the field name with no explicit type.

## Pointer (Index declaration)

Index declarations are parsed as `Pointer` AST nodes. They are **not emitted as C# today**. See [Entities](entities.md).

```ebnf
pointer = "Index", identifier, { identifier },
          [ pointer options ],
          [ "{", { note }, "}" ] ;
```

Identifiers after `Index` name the indexed columns.

## Annotations

```ebnf
note = "Note", string ;

property = identifier, expression ;
```

Recognized property keys: `Default`, `Length`, `Precision`, `Algorithm`. Arbitrary keys are accepted syntactically but are not consumed by the generator today.

## Options

```ebnf
field options = "[", field option, { ",", field option }, "]" ;

field option = "Required" | "NotNull" | ( "Not", "Null" )
             | "Unique"
             | "PrimaryKey" | ( "Primary", "Key" )
             | "AutoIncrement" | ( "Auto", "Increment" )
             | "BTree" | ( "B", "Tree" )
             | "Hash" ;

view options = "[", view option, { ",", view option }, "]" ;

view option = "Omit"
            | "OmitAll" | ( "Omit", "All" ) ;

pointer options = "[", pointer option, { ",", pointer option }, "]" ;

pointer option = "Unique"
               | "BTree" | ( "B", "Tree" )
               | "Hash" ;
```

All options are case-insensitive. Multi-word forms and their concatenated CamelCase equivalents are interchangeable. The parser normalizes by lowercasing and stripping spaces and underscores before matching.

## Composition

```ebnf
use = "Use", name list ;

name list = qualified name, { ",", qualified name } ;
```

## Expressions

```ebnf
expression = literal
           | function call
           | reference ;

function call = qualified name, "(", [ expression, { ",", expression } ], ")" ;

reference = qualified name ;
```

Disambiguation: literals are tested first; then function call (qualified name followed by `(`); then reference. There are no operators or precedence rules.

## Literals

```ebnf
literal = string | boolean | "null" | number ;

string = triple single string | triple double string | quoted string ;

triple single string = "'''", { any character }, "'''" ;

triple double string = '"""', { any character }, '"""' ;

quoted string = Terms.String() ;

number = Terms.Decimal() ;

boolean = "true" | "false" ;
```

String parsing delegates to Parlot's `Terms.String()` (handles standard single- and double-quoted strings). Number parsing delegates to Parlot's `Terms.Decimal()`. Triple-quoted forms are tried before standard quoted forms.

## Lexical

```ebnf
qualified name = identifier, { ".", identifier } ;

identifier = Terms.Identifier() ;

line comment = "//", { any character except newline }, ( newline | end of input ) ;

block comment = "/*", { any character }, "*/" ;
```

Comments are registered on the `Entity` and `Document` parsers via Parlot's `.WithComments(...)`. They are skipped transparently during parsing.

## See also

- [Types](types.md) — built-in type token table
- [Fields](fields.md) — field syntax and options reference
- [Traits](traits.md) — trait body and generator emission
- [Entities](entities.md) — entity body and generator emission
- [Enums](enums.md) — enum syntax and emission rules
- [Objects](objects.md) — Object block syntax (parsed, not emitted today)
- [Expressions](expressions.md) — literal, function call, and reference forms
