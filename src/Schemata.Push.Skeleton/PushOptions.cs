using System;

namespace Schemata.Push.Skeleton;

/// <summary>
///     Cross-transport delivery options. Transport-specific knobs travel through
///     <see cref="PushContext.Metadata" /> instead.
/// </summary>
public sealed record PushOptions
{
    /// <summary>The shared default options instance.</summary>
    public static readonly PushOptions Default = new();

    /// <summary>Relative delivery priority.</summary>
    public PushPriority Priority { get; init; } = PushPriority.Normal;

    /// <summary>Lifetime after which an undelivered message may be dropped.</summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>Replaces an earlier undelivered message sharing the same key.</summary>
    public string? CollapseKey { get; init; }

    /// <summary>Deduplicates redelivery across retries (e.g. an FCM message id).</summary>
    public string? DedupId { get; init; }
}
