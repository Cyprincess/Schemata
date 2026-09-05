using Schemata.Abstractions.Exceptions;

namespace Schemata.Messaging.Skeleton.Runtime;

/// <summary>
///     A request failed on the remote side of a request/reply hop. Carries only the stable
///     reason code the remote published — never the remote exception's message or stack.
/// </summary>
/// <param name="reason">Stable error code describing the failure category.</param>
/// <param name="message">Optional caller-facing diagnostic message.</param>
public sealed class RemoteRequestException(string reason, string? message) : SchemataException(500, null, message)
{
    /// <summary>Stable error code describing the failure category (e.g. <c>"cancelled"</c>, <c>"internal"</c>).</summary>
    public string Reason { get; } = reason;
}
