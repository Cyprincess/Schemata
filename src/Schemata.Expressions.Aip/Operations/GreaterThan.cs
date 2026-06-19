using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

/// <summary>
///     Represents the AIP greater-than comparator.
/// </summary>
public class GreaterThan : IBinary
{
    /// <summary>
    ///     The greater-than comparator token.
    /// </summary>
    public const char Char = '>';

    /// <summary>
    ///     Creates a greater-than comparator token at the supplied source position.
    /// </summary>
    public GreaterThan(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition    Position   { get; }
    public bool            IsConstant => false;
    public ExpressionType? Type       => ExpressionType.GreaterThan;

    #endregion

    public override string ToString() { return $"{Char}"; }
}
