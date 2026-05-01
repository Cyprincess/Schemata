namespace Schemata.Modeling.Generator.Expressions;

public sealed record ViewField(
    string?                    Type,
    bool                       Nullable,
    string                     Name,
    EquatableArray<ViewOption> Options,
    EquatableArray<Note>       Notes,
    EquatableArray<ViewField>  Children,
    IExpression?               Assignment
);
