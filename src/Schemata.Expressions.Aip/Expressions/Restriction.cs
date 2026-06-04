using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

public class Restriction : ISimple
{
    public Restriction(TextPosition position, IComparableArg comparable, (IBinary, IArg)? comparator) {
        Position   = position;
        Comparable = comparable;

        if (comparator is not null) {
            Comparator = comparator.Value.Item1;
            Arg        = comparator.Value.Item2;
        }
    }

    public IComparableArg Comparable { get; }

    public IBinary? Comparator { get; }

    public IArg? Arg { get; }

    #region ISimple Members

    public TextPosition Position   { get; }
    public bool         IsConstant => Comparable.IsConstant && Comparator is null;

    #endregion

    public override string? ToString() {
        return Comparator is not null ? $"[{Comparator} {Comparable} {Arg}]" : Comparable.ToString();
    }
}
