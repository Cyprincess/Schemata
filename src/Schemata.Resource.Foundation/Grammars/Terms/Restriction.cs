using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Terms;

public class Restriction : ISimple
{
    public Restriction(TextPosition position, IComparable comparable, (IBinary, IArg)? comparator) {
        Position = position;

        Comparable = comparable;

        if (comparator is not null) {
            Comparator = comparator.Value.Item1;
            Arg        = comparator.Value.Item2;
        }
    }

    public IComparable Comparable { get; }

    public IBinary? Comparator { get; }

    public IArg? Arg { get; }

    #region ISimple Members

    public TextPosition Position { get; }

    public bool IsConstant => Comparable.IsConstant && Comparator is null;

    public Expression? ToExpression(Container ctx) {
        var left = Comparable.ToExpression(ctx);
        if (Comparator is null || Arg is null) {
            return left;
        }

        var right = Arg.ToExpression(ctx);
        if (left is null || right is null) {
            return null;
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
