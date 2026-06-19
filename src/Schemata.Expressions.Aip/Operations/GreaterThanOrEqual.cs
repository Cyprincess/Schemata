using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

/// <summary>
///     Represents the AIP greater-than-or-equal comparator.
/// </summary>
public class GreaterThanOrEqual : IBinary
{
    /// <summary>
    ///     The greater-than-or-equal comparator token.
    /// </summary>
    public const string Name = ">=";

    /// <summary>
    ///     Creates a greater-than-or-equal comparator token at the supplied source position.
    /// </summary>
    public GreaterThanOrEqual(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition    Position   { get; }
    public bool            IsConstant => false;
    public ExpressionType? Type       => ExpressionType.GreaterThanOrEqual;

    #endregion

    public override string ToString() { return Name; }
}
