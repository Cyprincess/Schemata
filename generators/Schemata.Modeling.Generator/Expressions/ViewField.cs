namespace Schemata.Modeling.Generator.Expressions;

internal sealed record ViewField(
    string?                    Type,
    bool                       Nullable,
    string                     Name,
    EquatableArray<ViewOption> Options,
    EquatableArray<Note>       Notes,
    EquatableArray<ViewField>  Children,
    IExpression?               Assignment
);
