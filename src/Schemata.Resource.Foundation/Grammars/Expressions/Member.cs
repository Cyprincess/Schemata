using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Parlot;
using Schemata.Abstractions;
using Schemata.Resource.Foundation.Grammars.Values;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     Represents a member access expression (e.g. <c>entity.property</c>) in the filter grammar.
/// </summary>
public class Member : IComparableArg
{
    /// <summary>
    ///     Initializes a new member with a root value and optional field chain.
    /// </summary>
    /// <param name="position">The position in the source text.</param>
    /// <param name="value">The root value token.</param>
    /// <param name="fields">The chain of field accesses following the root.</param>
    public Member(TextPosition position, IValue value, IReadOnlyCollection<IField>? fields) {
        Position = position;

        Value = value;

        if (fields is not null) {
            Fields.AddRange(fields);
        }
    }

    /// <summary>
    ///     Gets the root value token of this member access.
    /// </summary>
    public IValue Value { get; }

    /// <summary>
    ///     Gets the chain of field accesses following the root value.
    /// </summary>
    public List<IField> Fields { get; } = [];

    #region IComparable Members

    public TextPosition Position { get; }

    public bool IsConstant => Value.IsConstant && Fields.Count == 0;

    public Expression ToExpression(Container ctx) {
        var expression = ToMemberExpression(ctx);

        if (Fields.Count <= 0) {
            return expression;
        }

        return BuildAccess(expression, Fields.LastOrDefault(), ctx);
    }

    #endregion

    /// <summary>
    ///     Builds the member access expression chain up to (but not including) the last field.
    /// </summary>
    /// <param name="ctx">The expression-building container.</param>
    /// <returns>The intermediate member expression.</returns>
    public Expression ToMemberExpression(Container ctx) {
        var expression = Value.ToExpression(ctx);

        if (expression is null) {
            throw new ParseException("Expect value", Value.Position);
        }

        if (Fields.Count <= 0) {
            return expression;
        }

        foreach (var field in Fields.Take(Fields.Count - 1)) {
            expression = BuildAccess(expression, field, ctx);
        }

        return expression;
    }

    public override string? ToString() {
        return Fields.Count > 0 ? $"{Value}.{string.Join('.', Fields)}" : Value.ToString();
    }

    internal Expression BuildAccess(Expression expression, IField? field, Container ctx) {
        if (field is null) {
            return expression;
        }

        var property = field switch {
            Text text       => text.Value,
            Integer integer => integer.Value.ToString(),
            var _           => throw new ParseException(SchemataResources.GetResourceString(SchemataResources.ST2005), field.Position),
        };

        if (typeof(IDictionary).IsAssignableFrom(expression.Type)) {
            return Expression.Property(expression, "Item", Expression.Constant(property));
        }

        if (typeof(IEnumerable).IsAssignableFrom(expression.Type)) {
            if (field is not Integer { Value: < int.MaxValue } integer) {
                throw new ParseException("Expect array index", field.Position);
            }

            var type = expression.Type.GetElementType() ?? expression.Type.GenericTypeArguments.FirstOrDefault();

            var at = ctx.GetMethod(typeof(Enumerable), "ElementAt", [type!],
                                   () => typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                                                           .Single(x => x.Name == "ElementAt"
                                                                     && x.GetParameters().Length == 2
                                                                     && x.GetParameters().Last().ParameterType == typeof(int))
                                                           .MakeGenericMethod(type!));

            return Expression.Call(at!, expression, Expression.Constant((int)integer.Value));
        }

        try {
            return Expression.PropertyOrField(expression, property);
        } catch (ArgumentException) {
            throw new ParseException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST2006), property), field.Position);
        }
    }
}
