using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

/// <summary>
///     Represents the AIP less-than comparator.
/// </summary>
public class LessThan : IBinary
{
    /// <summary>
    ///     The less-than comparator token.
    /// </summary>
    public const char Char = '<';

    /// <summary>
    ///     Creates a less-than comparator token at the supplied source position.
    /// </summary>
    public LessThan(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition    Position   { get; }
    public bool            IsConstant => false;
    public ExpressionType? Type       => ExpressionType.LessThan;

    #endregion

    public override string ToString() { return $"{Char}"; }
}
