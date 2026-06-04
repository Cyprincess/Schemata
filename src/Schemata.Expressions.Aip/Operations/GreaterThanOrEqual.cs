using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

public class GreaterThanOrEqual : IBinary
{
    public const string Name = ">=";

    public GreaterThanOrEqual(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition    Position   { get; }
    public bool            IsConstant => false;
    public ExpressionType? Type       => ExpressionType.GreaterThanOrEqual;

    #endregion

    public override string ToString() { return Name; }
}
