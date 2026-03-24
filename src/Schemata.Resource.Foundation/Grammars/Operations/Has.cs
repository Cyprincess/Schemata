using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
/// Represents the has operator (<c>:</c>) supporting presence checks, collection containment, dictionary key lookup, and string contains.
/// </summary>
public class Has : IBinary
{
    /// <summary>
    /// The character representing the has operator.
    /// </summary>
    public const char Char = ':';

    public Has(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression? ToExpression(Container ctx) { return null; }

    public ExpressionType? Type => null;

    public Expression? ToExpression(Expression left, Expression right, Container ctx) {
        if (right is ConstantExpression { Value: "*" }) {
            return BuildPresenceExpression(left);
        }

        if (typeof(IDictionary).IsAssignableFrom(left.Type)) {
            return BuildDictionaryContainsKey(left, right);
        }

        if (left.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(left.Type)) {
            return BuildCollectionContains(left, right, ctx);
        }

        if (left.Type == typeof(string)) {
            return BuildStringContains(left, right, ctx);
        }

        if (right.Type != left.Type) {
            right = Expression.Convert(right, left.Type);
        }

        return Expression.MakeBinary(ExpressionType.Equal, left, right);
    }

    #endregion

    private static Expression BuildPresenceExpression(Expression left) {
        if (left.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(left.Type)) {
            var elementType = left.Type.GetElementType()
                ?? left.Type.GenericTypeArguments.FirstOrDefault()
                ?? typeof(object);

            var method = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .First(m => m.Name == "Any" && m.GetParameters().Length == 1)
                .MakeGenericMethod(elementType);

            var notNull = Expression.NotEqual(left, Expression.Constant(null, left.Type));
            var any     = Expression.Call(method, left);
            return Expression.AndAlso(notNull, any);
        }

        if (!left.Type.IsValueType || Nullable.GetUnderlyingType(left.Type) is not null) {
            return Expression.NotEqual(left, Expression.Constant(null, left.Type));
        }

        return Expression.Constant(true);
    }

    private static Expression BuildDictionaryContainsKey(Expression left, Expression right) {
        var containsKey = left.Type.GetMethod("ContainsKey");
        if (containsKey is not null) {
            var keyType = containsKey.GetParameters()[0].ParameterType;
            if (right.Type != keyType) {
                right = Expression.Convert(right, keyType);
            }

            return Expression.Call(left, containsKey, right);
        }

        if (right.Type != typeof(object)) {
            right = Expression.Convert(right, typeof(object));
        }

        var contains = typeof(IDictionary).GetMethod("Contains", [typeof(object)])!;
        return Expression.Call(left, contains, right);
    }

    private static Expression BuildCollectionContains(Expression left, Expression right, Container ctx) {
        var elementType = left.Type.GetElementType()
            ?? left.Type.GenericTypeArguments.FirstOrDefault()
            ?? typeof(object);

        if (right.Type != elementType) {
            right = Expression.Convert(right, elementType);
        }

        var method = ctx.GetMethod(typeof(Enumerable), "Contains", [elementType],
            () => typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .Single(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                .MakeGenericMethod(elementType));

        return Expression.Call(method!, left, right);
    }

    private static Expression BuildStringContains(Expression left, Expression right, Container ctx) {
        if (right.Type != typeof(string)) {
            right = Expression.Call(right, "ToString", null);
        }

        var method = ctx.GetMethod(typeof(string), nameof(string.Contains), [typeof(string)]);
        return Expression.Call(left, method!, right);
    }

    public override string ToString() { return $"{Char}"; }
}
