namespace Schemata.Modeling.Generator.Expressions;

internal sealed record Document(
    string?                     Namespace,
    EquatableArray<Entity>      Entities,
    EquatableArray<Trait>       Traits,
    EquatableArray<Enumeration> Enumerations
);
