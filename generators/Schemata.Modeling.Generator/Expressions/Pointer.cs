namespace Schemata.Modeling.Generator.Expressions;

internal sealed record Pointer(
    EquatableArray<string>        Columns,
    EquatableArray<PointerOption> Options,
    EquatableArray<Note>          Notes
);
