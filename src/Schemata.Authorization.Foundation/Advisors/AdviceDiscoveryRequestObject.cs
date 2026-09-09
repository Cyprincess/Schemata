using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Advertises JAR support in the discovery document: the allow-list of JWS signing
///     algorithms and the <c>require_signed_request_object</c> flag,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9101.html#section-10.5">
///         RFC 9101: JWT-Secured Authorization Request (JAR) §10.5: Choice of Algorithms
///     </seealso>
///     .
/// </summary>
public sealed class AdviceDiscoveryRequestObject(IOptions<JwtSecuredAuthorizationRequestsOptions> options) : IDiscoveryAdvisor
{
    public int Order => AdviceDiscoveryTokenExchange.DefaultOrder + 20_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var algs = options.Value.SigningAlgorithms;
        if (algs.Count == 0 && !options.Value.RequireForAllClients) {
            return Task.FromResult(AdviseResult.Continue);
        }

        discovery.Document ??= new();
        if (algs.Count > 0) {
            discovery.Document.RequestObjectSigningAlgValuesSupported = algs.OrderBy(a => a).ToList();
        }

        discovery.Document.RequireSignedRequestObject = options.Value.RequireForAllClients;

        return Task.FromResult(AdviseResult.Continue);
    }
}