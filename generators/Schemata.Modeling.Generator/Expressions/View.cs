namespace Schemata.Modeling.Generator.Expressions;

public sealed record View(string Name, EquatableArray<Note> Notes, EquatableArray<ViewField> Fields);
