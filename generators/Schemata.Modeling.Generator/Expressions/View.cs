namespace Schemata.Modeling.Generator.Expressions;

internal sealed record View(string Name, EquatableArray<Note> Notes, EquatableArray<ViewField> Fields);
