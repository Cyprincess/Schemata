namespace Schemata.Messaging.Skeleton.Runtime;

/// <summary>AMQP headers that mark a request/reply message as a remote error envelope.</summary>
public static class RequestErrorHeaders
{
    /// <summary>Marks a reply whose body is a <see cref="RemoteRequestError" /> instead of a response payload.</summary>
    public const string RemoteError = "x-schemata-remote-error";
}