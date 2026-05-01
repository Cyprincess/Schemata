namespace Schemata.Modeling.Generator.Expressions;

public sealed record Pointer(
    EquatableArray<string>        Columns,
    EquatableArray<PointerOption> Options,
    EquatableArray<Note>          Notes
);
