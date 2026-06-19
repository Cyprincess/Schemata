using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

/// <summary>
///     Represents an AIP comparable expression with an optional comparator and argument.
/// </summary>
public class Restriction : ISimple
{
    /// <summary>
    ///     Creates a restriction from a comparable value and optional comparison.
    /// </summary>
    public Restriction(TextPosition position, IComparableArg comparable, (IBinary, IArg)? comparator) {
        Position   = position;
        Comparable = comparable;

        if (comparator is not null) {
            Comparator = comparator.Value.Item1;
            Arg        = comparator.Value.Item2;
        }
    }

    /// <summary>
    ///     Gets the expression being tested.
    /// </summary>
    public IComparableArg Comparable { get; }

    /// <summary>
    ///     Gets the comparator token when the restriction contains a comparison.
    /// </summary>
    public IBinary? Comparator { get; }

    /// <summary>
    ///     Gets the right-side argument when the restriction contains a comparison.
    /// </summary>
    public IArg? Arg { get; }

    #region ISimple Members

    public TextPosition Position   { get; }
    public bool         IsConstant => Comparable.IsConstant && Comparator is null;

    #endregion

    public override string? ToString() {
        return Comparator is not null ? $"[{Comparator} {Comparable} {Arg}]" : Comparable.ToString();
    }
}
