namespace Schemata.Messaging.Skeleton.Runtime;

/// <summary>AMQP headers that mark a request/reply message as a remote error envelope.</summary>
public static class RequestErrorHeaders
{
    /// <summary>Marks a reply whose body is a <see cref="RemoteRequestError" /> instead of a response payload.</summary>
    public const string RemoteError = "x-schemata-remote-error";
}

/// <summary>The stable, detail-free reason a remote request/reply hop failed.</summary>
/// <param name="Reason">Stable error code; never carries the remote exception's details.</param>
public sealed record RemoteRequestError(string Reason);
