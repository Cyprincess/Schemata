namespace Schemata.Messaging.Skeleton.Runtime;

/// <summary>The stable, detail-free reason a remote request/reply hop failed.</summary>
/// <param name="Reason">Stable error code; never carries the remote exception's details.</param>
public sealed record RemoteRequestError(string Reason);