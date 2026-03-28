using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>Handles token issuance for a specific OAuth 2.0 grant type at the token endpoint.</summary>
public interface IGrantHandler
{
    string GrantType { get; }

    Task<AuthorizationResult> HandleAsync(
        TokenRequest                       request,
        Dictionary<string, List<string?>>? headers,
        CancellationToken                  ct
    );
}
