namespace Schemata.Push.Skeleton;

/// <summary>Outcome of one <see cref="IPushTransport" /> handling a dispatch.</summary>
public enum TransportStatus
{
    /// <summary>No outcome recorded.</summary>
    Unspecified,

    /// <summary>The transport accepted and delivered the message.</summary>
    Sent,

    /// <summary>The transport does not handle this target and took no action.</summary>
    Skipped,

    /// <summary>The transport attempted delivery and failed.</summary>
    Failed,
}
