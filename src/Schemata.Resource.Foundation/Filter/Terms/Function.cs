using System.Collections.Generic;

namespace Schemata.Resource.Foundation.Filter.Terms;

public class Function : IComparable
{
    public Function(Member member, IReadOnlyCollection<IArg>? args) {
        Member = member;

        if (args is not null) {
            Args.AddRange(args);
        }
    }

    public Member Member { get; }

    public List<IArg> Args { get; } = [];

    #region IComparable Members

    public bool IsConstant => false;

    #endregion

    public override string ToString() {
        return $"{Member}({string.Join(',', Args)})";
    }
}
