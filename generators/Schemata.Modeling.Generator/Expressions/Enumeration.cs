namespace Schemata.Modeling.Generator.Expressions;

public sealed record Enumeration(string Name, EquatableArray<Note> Notes, EquatableArray<EnumValue> Values);
