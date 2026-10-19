using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Handles token issuance for a specific OAuth 2.0 grant type at the token endpoint.
///     Each implementation is identified by its <see cref="GrantType" /> string.
/// </summary>
public interface IGrantHandler
{
    /// <summary>OAuth 2.0 grant type identifier, e.g. <c>"authorization_code"</c>.</summary>
    string GrantType { get; }

    /// <summary>Processes a token request for this grant type.</summary>
    /// <param name="request">Token request carrying the grant parameters.</param>
    /// <param name="headers">HTTP request headers for client authentication.</param>
    /// <param name="proof">
    ///     The raw DPoP proof JWT header value extracted by the endpoint, or
    ///     <see langword="null" /> when the request carries no proof.
    /// </param>
    /// <param name="context">The dispatch's advisor context.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        string?                            proof,
        AdviceContext                      context,
        CancellationToken                  ct
    );
}
