namespace Schemata.Modeling.Generator.Expressions;

internal sealed record Enumeration(string Name, EquatableArray<Note> Notes, EquatableArray<EnumValue> Values);
