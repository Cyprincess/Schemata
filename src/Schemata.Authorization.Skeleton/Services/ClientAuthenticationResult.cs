namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     The outcome of client authentication: the located application, the mechanism that actually
///     produced the result, and whether that mechanism verified a credential. A <c>none</c>
///     identification carries <see cref="Authenticated" /> = <see langword="false" /> — locating a
///     public client by its identifier is not proof of confidential authority.
/// </summary>
/// <typeparam name="TApplication">The application entity type.</typeparam>
public sealed class ClientAuthenticationResult<TApplication>
    where TApplication : class
{
    /// <summary>The located application record.</summary>
    public required TApplication Application { get; init; }

    /// <summary>The client-authentication method that produced this result (e.g. <c>none</c>, <c>client_secret_basic</c>).</summary>
    public required string Method { get; init; }

    /// <summary>Whether a registered credential mechanism was verified, as opposed to identification only.</summary>
    public bool Authenticated { get; init; }
}
