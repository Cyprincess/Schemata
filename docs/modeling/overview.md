# Modeling

The Schemata Modeling Language (SKM) defines entities, traits, and enums in `.skm` files.
`Schemata.Modeling.Generator` is a Roslyn incremental source generator that parses each
`.skm` file at build time and emits C#.

## What the generator emits

`DocumentGenerator` walks the parsed document and emits three declaration kinds:

| Declaration | Generated C# |
| --- | --- |
| `Entity` | `public record` with one init-set auto-property per field; trait interfaces in the base list |
| `Trait` | `public interface I{Name}` with one property per field |
| `Enum` | `public enum` with one member per value |

`Object` blocks, `Index` declarations, field options, and field properties are parsed into
the AST and carried on their declarations, but produce no C# output. The parser accepts them
so a model can record indexing and projection intent; only entities, traits, and enums reach
the compiler.

## Document structure

An `.skm` file is an optional namespace declaration followed by any number of top-level
declarations:

```text
Namespace My.Project.Models

Trait Identifier {
    guid uid [primary key]
}

Entity Student : Identifier {
    string full_name [not null]
    int age
}

Enum EnrollmentStatus {
    Draft
    Enrolled
    Graduated
}
```

The `Namespace` declaration, when present, is the first declaration in the file. Keywords are
case-insensitive (`Entity`, `entity`, `ENTITY` are equivalent). Comments use `//` for a line
and `/* */` for a block.

## Activating the generator

Reference `Schemata.Modeling.Generator` as an analyzer and add each model file as
`<AdditionalFiles>`. The generator runs on every additional file whose path ends in `.skm`.

```xml
<ItemGroup>
  <PackageReference Include="Schemata.Modeling.Generator" PrivateAssets="all" />
  <AdditionalFiles Include="Models.skm" />
</ItemGroup>
```

## Language reference

| Topic | Description |
| --- | --- |
| [Types](types.md) | Built-in type tokens, nullable types, custom types |
| [Fields](fields.md) | Field syntax, options, properties, notes |
| [Traits](traits.md) | Reusable field groups, interface emission |
| [Entities](entities.md) | Entity definitions, composition, nested members |
| [Enums](enums.md) | Named constant types, value assignment |
| [Objects](objects.md) | `Object` projection blocks (parsed, not emitted) |
| [Expressions](expressions.md) | Literals, function calls, references |
| [Grammar](grammar.md) | Complete EBNF, derived from `Parser.cs` |
