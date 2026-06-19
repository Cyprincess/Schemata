namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL unary operation.
/// </summary>
public sealed class CelUnary : CelNode
{
    /// <summary>
    ///     Creates a unary operation from an operator and operand.
    /// </summary>
    public CelUnary(string op, CelNode operand) {
        Operator = op;
        Operand  = operand;
    }

    /// <summary>
    ///     Gets the CEL unary operator token.
    /// </summary>
    public string Operator { get; }

    /// <summary>
    ///     Gets the operand expression.
    /// </summary>
    public CelNode Operand { get; }
}
