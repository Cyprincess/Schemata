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

    /// <inheritdoc />
    public TextPosition Position { get; }

    /// <inheritdoc />
    public bool IsConstant => true;

    /// <inheritdoc />
    public Expression ToExpression(Container ctx) { return Expression.Constant(Value); }

    #endregion

    /// <inheritdoc />
    public override string ToString() { return Value.ToString(); }
}
