using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

public sealed class FlowBranch
{
    public FlowBranch(Activity entry, Activity exit) {
        Entry = entry;
        Exit  = exit;
    }

    public Activity Entry { get; }

    public Activity Exit { get; }

    public static implicit operator FlowBranch(Activity a) { return new(a, a); }

    public static implicit operator FlowBranch(ActivityBehavior d) { return new(d.Activity, d.LastTarget); }
}
