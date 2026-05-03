using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Contexts;

/// <summary>
///     Data carrier for the discovery endpoint pipeline.
///     Consumed by <see cref="Advisors.IDiscoveryAdvisor" />.
/// </summary>
public sealed class DiscoveryContext
{
    /// <summary>Issuer URL of the authorization server.</summary>
    public string? Issuer { get; set; }

    /// <summary>The discovery document being built.</summary>
    public DiscoveryDocument? Document { get; set; }

    /// <summary>
    ///     Whether the server supports the <c>iss</c> parameter in authorization responses.
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9207.html">
    ///         RFC 9207: OAuth 2.0 Authorization Server Issuer
    ///         Identification
    ///     </seealso>
    /// </summary>
    public bool SupportsAuthorizationResponseIss { get; set; }

    /// <summary>Whether front-channel logout is supported.</summary>
    public bool SupportsFrontChannelLogout { get; set; }

    /// <summary>Whether the server includes the <c>sid</c> claim in front-channel logout requests.</summary>
    public bool SupportsFrontChannelSession { get; set; }

    /// <summary>Whether back-channel logout is supported.</summary>
    public bool SupportsBackChannelLogout { get; set; }

    /// <summary>Whether the server includes the <c>sid</c> claim in back-channel logout tokens.</summary>
    public bool SupportsBackChannelSession { get; set; }

    /// <summary>Whether any <see cref="Handlers.ITokenExchangeHandler{TApplication}" /> implementations are registered.</summary>
    public bool HasTokenExchangeHandlers { get; set; }
}
