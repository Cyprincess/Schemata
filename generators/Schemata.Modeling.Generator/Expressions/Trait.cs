namespace Schemata.Modeling.Generator.Expressions;

public sealed record Trait(
    string                 Name,
    EquatableArray<string> Bases,
    EquatableArray<Note>   Notes,
    EquatableArray<Use>    Uses,
    EquatableArray<Field>  Fields
);
