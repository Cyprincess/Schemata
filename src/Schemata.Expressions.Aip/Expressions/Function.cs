using System.Collections.Generic;
using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

public class Function : IComparableArg
{
    public Function(TextPosition position, Member member, IReadOnlyCollection<IArg>? args) {
        Position = position;
        Member   = member;

        if (args is not null) {
            Args.AddRange(args);
        }
    }

    public Member Member { get; }

    public List<IArg> Args { get; } = [];

    #region IComparableArg Members

    public TextPosition Position   { get; }
    public bool         IsConstant => false;

    #endregion

    public override string ToString() { return $"{Member}({string.Join(",", Args)})"; }
}
