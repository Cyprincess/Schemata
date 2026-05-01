namespace Schemata.Modeling.Generator.Expressions;

public sealed record Entity(
    string                      Name,
    EquatableArray<string>      Bases,
    EquatableArray<Note>        Notes,
    EquatableArray<Use>         Uses,
    EquatableArray<Enumeration> Enumerations,
    EquatableArray<Trait>       Traits,
    EquatableArray<View>        Views,
    EquatableArray<Pointer>     Pointers,
    EquatableArray<Field>       Fields
);
