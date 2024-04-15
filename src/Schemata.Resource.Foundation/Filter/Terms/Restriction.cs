namespace Schemata.Resource.Foundation.Filter.Terms;

public class Restriction : ISimple
{
    public Restriction(IComparable comparable, (IBinary, IArg)? comparator) {
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

    public bool IsConstant => Comparable.IsConstant && Comparator is null;

    #endregion

    public override string? ToString() {
        return Comparator is not null ? $"[{Comparator} {Comparable} {Arg}]" : Comparable.ToString();
    }
}
