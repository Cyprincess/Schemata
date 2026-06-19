namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL conditional expression.
/// </summary>
public sealed class CelConditional : CelNode
{
    /// <summary>
    ///     Creates a conditional expression from condition, true branch, and false branch nodes.
    /// </summary>
    public CelConditional(CelNode condition, CelNode whenTrue, CelNode whenFalse) {
        Condition = condition;
        WhenTrue  = whenTrue;
        WhenFalse = whenFalse;
    }

    /// <summary>
    ///     Gets the condition expression.
    /// </summary>
    public CelNode Condition { get; }

    /// <summary>
    ///     Gets the expression evaluated when the condition is true.
    /// </summary>
    public CelNode WhenTrue { get; }

    /// <summary>
    ///     Gets the expression evaluated when the condition is false.
    /// </summary>
    public CelNode WhenFalse { get; }
}
