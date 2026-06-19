using System.Collections.Generic;

namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL member function or macro call.
/// </summary>
public sealed class CelMemberCall : CelNode
{
    /// <summary>
    ///     Creates a member call for the target expression.
    /// </summary>
    public CelMemberCall(CelNode target, string name, IReadOnlyList<CelNode> args) {
        Target = target;
        Name   = name;
        Args   = args;
    }

    /// <summary>
    ///     Gets the expression that receives the member call.
    /// </summary>
    public CelNode Target { get; }

    /// <summary>
    ///     Gets the member function or macro name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the call arguments.
    /// </summary>
    public IReadOnlyList<CelNode> Args { get; }
}
