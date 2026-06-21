using System.Collections.Generic;

namespace Schemata.Push.Skeleton;

/// <summary>
///     Per-dispatch context flowing through the <see cref="Advisors.IPushSendAdvisor" /> pipeline and into
///     each <see cref="IPushTransport" />. The message is carried as <see cref="object" /> so the
///     advisor pipeline targets one non-generic type; transports cast or reflect as needed.
/// </summary>
public class PushContext
{
    /// <summary>Creates a dispatch context for <paramref name="message" /> aimed at <paramref name="target" />.</summary>
    /// <param name="message">The message payload; transports interpret it.</param>
    /// <param name="target">The dispatch target transports filter on.</param>
    public PushContext(object message, PushTarget target) {
        Message = message;
        Target  = target;
    }

    /// <summary>The message payload.</summary>
    public object Message { get; }

    /// <summary>The dispatch target.</summary>
    public PushTarget Target { get; }

    /// <summary>Cross-transport delivery options.</summary>
    public PushOptions Options { get; init; } = PushOptions.Default;

    /// <summary>Transport-specific metadata keyed by transport-defined names.</summary>
    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();
}
