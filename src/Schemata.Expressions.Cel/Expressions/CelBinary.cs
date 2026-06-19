namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL binary operation.
/// </summary>
public sealed class CelBinary : CelNode
{
    /// <summary>
    ///     Creates a binary operation from an operator and two operands.
    /// </summary>
    public CelBinary(string op, CelNode left, CelNode right) {
        Operator = op;
        Left     = left;
        Right    = right;
    }

    /// <summary>
    ///     Gets the CEL binary operator token.
    /// </summary>
    public string Operator { get; }

    /// <summary>
    ///     Gets the left operand.
    /// </summary>
    public CelNode Left { get; }

    /// <summary>
    ///     Gets the right operand.
    /// </summary>
    public CelNode Right { get; }
}
