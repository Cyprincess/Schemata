using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>One arm of <see cref="ActivityBehavior.Fork" /> identified by its entry and exit activities.</summary>
public sealed class FlowBranch
{
    /// <summary>Creates a flow branch spanning <paramref name="entry" /> to <paramref name="exit" />.</summary>
    public FlowBranch(Activity entry, Activity exit) {
        Entry = entry;
        Exit  = exit;
    }

    /// <summary>Activity the branch enters at.</summary>
    public Activity Entry { get; }

    /// <summary>Activity the branch terminates at.</summary>
    public Activity Exit { get; }

    /// <summary>Adopts a single-activity branch from <paramref name="a" />.</summary>
    public static implicit operator FlowBranch(Activity a) { return new(a, a); }

    /// <summary>Adopts a branch spanning the activity range of <paramref name="d" />.</summary>
    public static implicit operator FlowBranch(ActivityBehavior d) { return new(d.Activity, d.LastTarget); }
}
