using System.Collections.Generic;

namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL global function or macro call.
/// </summary>
public sealed class CelCall : CelNode
{
    /// <summary>
    ///     Creates a call with its name and arguments.
    /// </summary>
    public CelCall(string name, IReadOnlyList<CelNode> args) {
        Name = name;
        Args = args;
    }

    /// <summary>
    ///     Gets the function or macro name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the call arguments.
    /// </summary>
    public IReadOnlyList<CelNode> Args { get; }
}
