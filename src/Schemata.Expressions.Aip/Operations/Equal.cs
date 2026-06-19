using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

/// <summary>
///     Represents the AIP equality comparator.
/// </summary>
public class Equal : IBinary
{
    /// <summary>
    ///     The equality comparator token.
    /// </summary>
    public const char Char = '=';

    /// <summary>
    ///     Creates an equality comparator token at the supplied source position.
    /// </summary>
    public Equal(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition    Position   { get; }
    public bool            IsConstant => false;
    public ExpressionType? Type       => null;

    #endregion

    public override string ToString() { return $"{Char}"; }
}
