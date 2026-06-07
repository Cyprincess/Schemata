# Modeling

The Schemata Modeling Language (SKM) is a domain-specific language for defining entities, traits, and enums in `.skm` files. The `Schemata.Modeling.Generator` Roslyn source generator reads these files at build time and emits C# code.

## What the generator emits today

The parser accepts a broad grammar, but the generator is intentionally narrow. Only three declaration kinds produce C# output:

| Declaration | Generated C# |
| --- | --- |
| `Entity` | `public record Name(...)` with auto-properties; base list includes trait interfaces |
| `Trait` | `public interface IName` with read/write properties for each field |
| `Enum` | `public enum Name` with sequential integer values |

Everything else — `Object` blocks, `View` blocks, `Index` declarations, `Pointer` declarations, and field options/properties — is parsed and included in the AST but **not emitted**. These constructs are reserved for future codegen. Do not rely on them producing output today.

## Document structure

An SKM file (`.skm`) is a document consisting of an optional namespace declaration followed by any number of top-level declarations:

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

The `Namespace` declaration, if present, must be the first non-comment element in the file. Keywords are case-insensitive: `Entity`, `entity`, and `ENTITY` are equivalent. Comments use C-style syntax (`//` line comments and `/* */` block comments).

## Activating the generator

Add the `.skm` file as `<AdditionalFiles>` in your project, or reference a `Complex.Targets` meta-package that sets this up automatically:

```xml
<ItemGroup>
  <AdditionalFiles Include="Models.skm" />
</ItemGroup>
```

The generator only activates when `Schemata.Advice.AdvicePipeline\`1` is present in the compilation, so reference packs alone won't trigger spurious output.

## Language reference

| Topic | Description |
| --- | --- |
| [Types](types.md) | Built-in type mappings, nullable types, custom types |
| [Fields](fields.md) | Field syntax, options, properties, notes |
| [Traits](traits.md) | Reusable field groups, interface emission |
| [Entities](entities.md) | Entity definitions, inheritance, nested elements |
| [Enums](enums.md) | Named constant types, emission rules |
| [Objects](objects.md) | Object blocks (parsed, not emitted today) |
| [Expressions](expressions.md) | Literals, function calls, references |
| [Grammar](grammar.md) | Complete EBNF reference |

## See also

- [Guides: Getting Started](../guides/getting-started.md) — defines the `Student` entity used throughout the guides
- [Documents: Entity Traits](../documents/entity/traits.md) — trait interfaces the generator can implement
- [Cookbook: Module Packaging](../cookbook/module-packaging.md) — packaging generated entities in a module assembly
