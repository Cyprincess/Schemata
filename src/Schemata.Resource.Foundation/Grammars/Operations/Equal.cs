using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Operations;

/// <summary>
/// Represents the equality operator (<c>=</c>) with wildcard pattern support (<c>*</c> for prefix, suffix, and contains matching).
/// </summary>
public class Equal : IBinary
{
    /// <summary>
    /// The character representing the equality operator.
    /// </summary>
    public const char Char = '=';

    public Equal(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression? ToExpression(Container ctx) { return null; }

    public Expression? ToExpression(Expression left, Expression right, Container ctx) {
        if (right is ConstantExpression { Value: string pattern } && pattern.Contains('*')) {
            return BuildWildcardExpression(left, pattern, ctx);
        }

        if (right.Type != left.Type) {
            right = Expression.Convert(right, left.Type);
        }

        return Expression.MakeBinary(ExpressionType.Equal, left, right);
    }

    public ExpressionType? Type => null;

    #endregion

    private static Expression BuildWildcardExpression(Expression left, string pattern, Container ctx) {
        if (pattern == "*") {
            return Expression.NotEqual(left, Expression.Constant(null, left.Type));
        }

        var leading = pattern.StartsWith('*');
        var trailing   = pattern.EndsWith('*');

        if (leading && trailing && pattern.Length > 2) {
            var sub    = pattern[1..^1];
            var method = ctx.GetMethod(typeof(string), nameof(string.Contains), [typeof(string)]);
            return Expression.Call(left, method!, Expression.Constant(sub));
        }

        if (trailing) {
            var prefix = pattern[..^1];
            var method = ctx.GetMethod(typeof(string), nameof(string.StartsWith), [typeof(string)]);
            return Expression.Call(left, method!, Expression.Constant(prefix));
        }

        if (leading) {
            var suffix = pattern[1..];
            var method = ctx.GetMethod(typeof(string), nameof(string.EndsWith), [typeof(string)]);
            return Expression.Call(left, method!, Expression.Constant(suffix));
        }

        return Expression.MakeBinary(ExpressionType.Equal, left, Expression.Constant(pattern));
    }

    public override string ToString() { return $"{Char}"; }
}
