# Grammar

This is the complete EBNF grammar for the Schemata Modeling Language (SKM).

Whitespace (spaces, tabs, newlines) and comments are freely permitted between any two tokens and are not shown in the production rules. At least one whitespace character is required between adjacent identifier or keyword tokens. Keywords are matched case-insensitively.

## Document

```ebnf
model = [ namespace ], { declaration } ;

declaration = entity
            | trait
            | enumeration ;
```

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
              "{", { [","], ( note | enum value ) }, "}" ;

enum value = identifier,
             [ "=", literal ],
             [ "{", { note }, "}" ] ;
```

Base lists and `Use` declarations are semantically equivalent. `Entity Foo : Bar, Baz` is sugar for declaring `Use Bar, Baz` at the top of the body.

## Field

```ebnf
field = type specifier, identifier,
        [ field options ],
        [ "{", { note | property }, "}" ] ;

type specifier = qualified name, [ "?" ] ;
```

## View (Object Block)

```ebnf
view = "Object", identifier,
       "{", { note | view field }, "}" ;

view field = qualified name, [ "?" ],
             [ identifier ],
             [ view options ],
             [ "{", { note | view field }, "}" ],
             [ "=", expression ] ;
```

Type inference for view fields (in priority order):

1. Explicit type — type specifier is present
2. Expression type — `= expression` return type
3. Inherited type — inferred from the same-named field in the parent entity

## Pointer (Index)

```ebnf
pointer = "Index", identifier, { identifier },
          [ pointer options ],
          [ "{", { note }, "}" ] ;
```

Identifiers after `Index` name the indexed columns. The index name is derived: `IX_{Entity}_{Col1}_{Col2}_...`

## Annotations

```ebnf
note = "Note", string ;

property = identifier, expression ;
```

Recognized property keys: `Default`, `Length`, `Precision`, `Algorithm`. Arbitrary keys are accepted syntactically.

## Options

```ebnf
field options = "[", field option, { ",", field option }, "]" ;

field option = "Required" | ( "Not", "Null" )
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

All options are case-insensitive. Multi-word forms and their concatenated CamelCase equivalents are interchangeable.

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

Disambiguation: literals are tested first; then function call (qualified name followed by `(`); then reference.

## Literals

```ebnf
literal = string | boolean | "null" | number ;

string = multiline string | quoted string ;

multiline string = "'''", { any character }, "'''"
                 | '"""', { any character }, '"""' ;

quoted string = "'", { character except unescaped "'" }, "'"
              | '"', { character except unescaped '"' }, '"' ;

number = digit, { digit },
         [ ".", digit, { digit } ] ;

boolean = "true" | "false" ;
```

## Lexical

```ebnf
qualified name = identifier, { ".", identifier } ;

identifier = ( letter | "_" ), { letter | digit | "_" } ;

letter = Unicode letter or ASCII A-Z, a-z ;

digit = ASCII digit 0-9 ;

line comment = "//", { any character except newline }, ( newline | end of input ) ;

block comment = "/*", { any character }, "*/" ;
```
