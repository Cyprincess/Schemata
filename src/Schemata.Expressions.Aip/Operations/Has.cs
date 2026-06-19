using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

/// <summary>
///     Represents the AIP has comparator.
/// </summary>
public class Has : IBinary
{
    /// <summary>
    ///     The has comparator token.
    /// </summary>
    public const char Char = ':';

    /// <summary>
    ///     Creates a has comparator token at the supplied source position.
    /// </summary>
    public Has(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition    Position   { get; }
    public bool            IsConstant => false;
    public ExpressionType? Type       => null;

    #endregion

    public override string ToString() { return $"{Char}"; }
}
