# Grammar

The complete grammar for SKM, derived from `Parser.cs`. Whitespace and comments are permitted
between any two tokens and are not shown in the production rules; at least one whitespace
character separates adjacent identifier or keyword tokens. Keywords are case-insensitive.

## Document structure

```ebnf
document = [ namespace ], { declaration } ;

declaration = entity
            | trait
            | enumeration ;
```

`Namespace`, when present, is the first declaration. `Entity`, `Trait`, and `Enum` are the only
top-level declarations; `Object` blocks and `Index` declarations are nested inside entity bodies.

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

A base list is equivalent to a `Use` declaration: `Entity Foo : Bar, Baz` is sugar for
`Use Bar, Baz`. Both resolve through `EntityGenerator.GenerateUses`.

## Field

```ebnf
field = type specifier, identifier,
        [ field options ],
        [ "{", { note | property }, "}" ] ;

type specifier = qualified name, [ "?" ] ;
```

Field names are written in `snake_case` and converted to PascalCase by `Utilities.ToCamelCase`.

## View (Object block)

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

The parser distinguishes typed from untyped by the token after the second identifier: `[`, `{`,
or `=` confirms the first qualified name is the type.

## Pointer (Index declaration)

```ebnf
pointer = "Index", identifier, { identifier },
          [ pointer options ],
          [ "{", { note }, "}" ] ;
```

The identifiers after `Index` name the indexed columns.

## Annotations

```ebnf
note = "Note", string ;

property = identifier, expression ;
```

Recognized property keys are `Default`, `Length`, `Precision`, and `Algorithm`; any other key is
accepted syntactically.

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

The parser lowercases each option and strips spaces and underscores before matching, so a
multi-word form and its concatenated form are interchangeable.

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

Literals are tested first, then function call, then reference. There are no operators or
precedence.

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

Standard quoting delegates to Parlot's `Terms.String()` and numbers to `Terms.Decimal()`. The
triple-quoted forms are tried first.

## Lexical

```ebnf
qualified name = identifier, { ".", identifier } ;

identifier = Terms.Identifier() ;

line comment = "//", { any character except newline }, ( newline | end of input ) ;

block comment = "/*", { any character }, "*/" ;
```

Comments are registered on the `Entity` and `Document` parsers through Parlot's `.WithComments`
and skipped during parsing.
