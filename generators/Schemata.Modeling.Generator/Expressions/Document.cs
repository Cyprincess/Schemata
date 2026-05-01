namespace Schemata.Modeling.Generator.Expressions;

public sealed record Document(
    string?                     Namespace,
    EquatableArray<Entity>      Entities,
    EquatableArray<Trait>       Traits,
    EquatableArray<Enumeration> Enumerations
);
