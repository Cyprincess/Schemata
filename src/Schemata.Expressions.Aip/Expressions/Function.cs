using System.Collections.Generic;
using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

/// <summary>
///     Represents an AIP function call argument.
/// </summary>
public class Function : IComparableArg
{
    /// <summary>
    ///     Creates a function call with its member path and arguments.
    /// </summary>
    public Function(TextPosition position, Member member, IReadOnlyCollection<IArg>? args) {
        Position = position;
        Member   = member;

        if (args is not null) {
            Args.AddRange(args);
        }
    }

    /// <summary>
    ///     Gets the function name or member path.
    /// </summary>
    public Member Member { get; }

    /// <summary>
    ///     Gets the function arguments.
    /// </summary>
    public List<IArg> Args { get; } = [];

    #region IComparableArg Members

    public TextPosition Position   { get; }
    public bool         IsConstant => false;

    #endregion

    public override string ToString() { return $"{Member}({string.Join(",", Args)})"; }
}
