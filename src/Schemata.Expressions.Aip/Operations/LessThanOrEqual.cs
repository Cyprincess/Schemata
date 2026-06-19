using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

/// <summary>
///     Represents the AIP less-than-or-equal comparator.
/// </summary>
public class LessThanOrEqual : IBinary
{
    /// <summary>
    ///     The less-than-or-equal comparator token.
    /// </summary>
    public const string Name = "<=";

    /// <summary>
    ///     Creates a less-than-or-equal comparator token at the supplied source position.
    /// </summary>
    public LessThanOrEqual(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition    Position   { get; }
    public bool            IsConstant => false;
    public ExpressionType? Type       => ExpressionType.LessThanOrEqual;

    #endregion

    public override string ToString() { return Name; }
}
