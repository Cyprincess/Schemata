namespace Schemata.Push.Skeleton;

/// <summary>Outcome reported by a single <see cref="IPushTransport" /> for one dispatch.</summary>
/// <param name="Transport">The reporting transport's <see cref="IPushTransport.Name" />.</param>
/// <param name="Status">The delivery outcome.</param>
/// <param name="Address">The obfuscated delivery address, when applicable.</param>
/// <param name="Provider">The backend-assigned message reference, when applicable.</param>
/// <param name="Error">The failure reason when <see cref="Status" /> is <see cref="TransportStatus.Failed" />.</param>
public sealed record TransportResult(
    string          Transport,
    TransportStatus Status,
    string?         Address  = null,
    string?         Provider = null,
    string?         Error    = null
)
{
    /// <summary>Creates a <see cref="TransportStatus.Sent" /> result.</summary>
    public static TransportResult Sent(string transport, string? address = null, string? provider = null) {
        return new(transport, TransportStatus.Sent, address, provider);
    }

    /// <summary>Creates a <see cref="TransportStatus.Skipped" /> result.</summary>
    public static TransportResult Skipped(string transport) {
        return new(transport, TransportStatus.Skipped);
    }

    /// <summary>Creates a <see cref="TransportStatus.Failed" /> result.</summary>
    public static TransportResult Failed(string transport, string error, string? address = null) {
        return new(transport, TransportStatus.Failed, address, Error: error);
    }
}
