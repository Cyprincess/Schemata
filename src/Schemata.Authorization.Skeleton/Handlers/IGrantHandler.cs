using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
