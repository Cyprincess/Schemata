using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     A comparison restriction (e.g., <c>field = value</c>).
///     When no comparator is present, the bare comparable expression is returned as-is,
///     which callers interpret as an existence/presence check.
/// </summary>
public class Restriction : ISimple
{
    /// <summary>
    ///     Initializes a new restriction.
    /// </summary>
    /// <param name="position">The position in the source text.</param>
    /// <param name="comparable">The left-hand comparable expression.</param>
    /// <param name="comparator">The optional comparator and right-hand argument.</param>
    public Restriction(TextPosition position, IComparableArg comparable, (IBinary, IArg)? comparator) {
        Position = position;

        Comparable = comparable;

        if (comparator is not null) {
            Comparator = comparator.Value.Item1;
            Arg        = comparator.Value.Item2;
        }
    }

    /// <summary>
    ///     Gets the left-hand comparable expression.
    /// </summary>
    public IComparableArg Comparable { get; }

    /// <summary>
    ///     Gets the comparator operator, or <see langword="null" /> for bare expressions.
    /// </summary>
    public IBinary? Comparator { get; }

    /// <summary>
    ///     Gets the right-hand argument, or <see langword="null" /> for bare expressions.
    /// </summary>
    public IArg? Arg { get; }

    #region ISimple Members

    public TextPosition Position { get; }

    public bool IsConstant => Comparable.IsConstant && Comparator is null;

    public Expression? ToExpression(Container ctx) {
        var left = Comparable.ToExpression(ctx);
        if (left is null) {
            throw new ParseException("Except comparable", Comparable.Position);
        }

        if (Comparator is null || Arg is null) {
            return left;
        }

        var right = Arg.ToExpression(ctx);
        if (right is null) {
            throw new ParseException("Except arg", Arg.Position);
        }

        if (Comparator.Type is null) {
            return Comparator.ToExpression(left, right, ctx);
        }

        if (right.Type != left.Type) {
            right = Expression.Convert(right, left.Type);
        }

        return Expression.MakeBinary(Comparator.Type.Value, left, right);
    }

    #endregion

    public override string? ToString() {
        return Comparator is not null ? $"[{Comparator} {Comparable} {Arg}]" : Comparable.ToString();
    }
}
