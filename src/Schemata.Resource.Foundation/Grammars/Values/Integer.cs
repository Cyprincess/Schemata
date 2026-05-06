using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Values;

/// <summary>
///     An integer literal.
/// </summary>
public class Integer : IValue
{
    /// <summary>
    ///     Initializes a new integer literal.
    /// </summary>
    public Integer(TextPosition position, long value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    ///     Gets the integer value.
    /// </summary>
    public long Value { get; }

    #region IValue Members

    object IValue.Value => Value;

    public TextPosition Position { get; }

    public bool IsConstant => true;

    public Expression ToExpression(Container ctx) { return Expression.Constant(Value); }

    #endregion

    public override string ToString() { return Value.ToString(); }
}
