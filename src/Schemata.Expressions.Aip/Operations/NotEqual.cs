using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

/// <summary>
///     Represents the AIP not-equal comparator.
/// </summary>
public class NotEqual : IBinary
{
    /// <summary>
    ///     The not-equal comparator token.
    /// </summary>
    public const string Name = "!=";

    /// <summary>
    ///     Creates a not-equal comparator token at the supplied source position.
    /// </summary>
    public NotEqual(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition    Position   { get; }
    public bool            IsConstant => false;
    public ExpressionType? Type       => ExpressionType.NotEqual;

    #endregion

    public override string ToString() { return Name; }
}
