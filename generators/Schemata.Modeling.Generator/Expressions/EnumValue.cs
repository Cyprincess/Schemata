namespace Schemata.Modeling.Generator.Expressions;

public sealed record EnumValue(string Name, IExpression? Assignment, EquatableArray<Note> Notes);
