using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip.Operations;

public class Equal : IBinary
{
    public const char Char = '=';

    public Equal(TextPosition position) { Position = position; }

    #region IBinary Members

    public TextPosition    Position   { get; }
    public bool            IsConstant => false;
    public ExpressionType? Type       => null;

    #endregion

    public override string ToString() { return $"{Char}"; }
}
