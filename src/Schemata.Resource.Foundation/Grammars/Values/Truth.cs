using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars.Values;

/// <summary>
/// Represents a boolean literal value (TRUE/FALSE) in the filter grammar.
/// </summary>
public class Truth : IValue
{
    public Truth(TextPosition position, bool value) {
        Value    = value;
        Position = position;
    }

    /// <summary>
    /// Gets the boolean value.
    /// </summary>
    public bool Value { get; }

    #region IValue Members

    object IValue.Value => Value;

    public TextPosition Position { get; }

    public bool IsConstant => true;

    public Expression ToExpression(Container ctx) { return Expression.Constant(Value); }

    #endregion

    public override string ToString() { return Value ? "\u2611" : "\u2612"; }
}
