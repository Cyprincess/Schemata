namespace Schemata.Modeling.Generator.Expressions;

internal sealed record EnumValue(string Name, IExpression? Assignment, EquatableArray<Note> Notes);
