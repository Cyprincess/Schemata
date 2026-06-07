using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

public sealed class Branch
{
    public Branch(Activity entry, IConditionExpression? condition = null, bool isDefault = false) {
        Entry     = entry;
        Exit      = entry;
        Condition = condition;
        IsDefault = isDefault;
    }

    public Activity Entry { get; }

    public Activity Exit { get; private set; }

    public IConditionExpression? Condition { get; }

    public bool IsDefault { get; }

    public Branch Go(Activity target) {
        Exit = target;
        return this;
    }
}
