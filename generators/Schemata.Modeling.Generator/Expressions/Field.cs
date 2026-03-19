namespace Schemata.Modeling.Generator.Expressions;

internal sealed record Field(
    string                      Type,
    bool                        Nullable,
    string                      Name,
    EquatableArray<FieldOption> Options,
    EquatableArray<Note>        Notes,
    EquatableArray<Property>    Properties
);
