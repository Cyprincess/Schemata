namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Payload of a registration access token: the client the token is bound to.
/// </summary>
public sealed class RegistrationTokenPayload
{
    /// <summary>The registered <c>client_id</c>.</summary>
    public string? ClientId { get; set; }

    /// <summary>Unix seconds the token was issued.</summary>
    public long IssuedAt { get; set; }
}
