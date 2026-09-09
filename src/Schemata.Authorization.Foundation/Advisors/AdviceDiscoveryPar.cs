using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Advertises the pushed authorization request endpoint and the
///     <c>require_pushed_authorization_requests</c> flag in the discovery document,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9126.html#section-5">
///         RFC 9126: OAuth 2.0 Pushed Authorization Requests §5: Authorization Server Metadata
///     </seealso>
///     .
/// </summary>
public sealed class AdviceDiscoveryPar(IOptions<PushedAuthorizationRequestsOptions> options) : IDiscoveryAdvisor
{
    public int Order => AdviceDiscoveryTokenExchange.DefaultOrder + 10_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;

        discovery.Document                            ??= new();
        discovery.Document.PushedAuthorizationRequestEndpoint = $"{issuer}{Endpoints.Par}";
        discovery.Document.RequirePushedAuthorizationRequests = options.Value.RequireForAllClients;

        return Task.FromResult(AdviseResult.Continue);
    }
}