using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Advertises the configured signed and encrypted JAR capabilities.</summary>
public sealed class AdviceDiscoveryRequestObject<TSecurity>(
    RequestObjectDecrypter<TSecurity> decrypter,
    IOptions<JwtSecuredAuthorizationRequestsOptions> options,
    IExternalRequestObjectResolver? external = null
) : IDiscoveryAdvisor
    where TSecurity : SchemataSecurity, new()
{
    public int Order => AdviceDiscoveryTokenExchange.DefaultOrder + 20_000_000;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext ctx,
        DiscoveryContext discovery,
        CancellationToken ct = default
    ) {
        discovery.Document ??= new();
        discovery.Document.RequestParameterSupported = true;

        var signing = options.Value.SigningAlgorithms;
        if (signing.Count > 0) {
            discovery.Document.RequestObjectSigningAlgValuesSupported = signing.OrderBy(value => value).ToList();
        }
        if (signing.Count > 0 || options.Value.RequireForAllClients) {
            discovery.Document.RequireSignedRequestObject = options.Value.RequireForAllClients;
        }

        if (options.Value.ContentEncryptionAlgorithms.Count > 0) {
            var encryption = await decrypter.GetSupportedAlgorithmsAsync(ct);
            if (encryption.Count > 0) {
                discovery.Document.RequestObjectEncryptionAlgValuesSupported = encryption.ToList();
                discovery.Document.RequestObjectEncryptionEncValuesSupported = options.Value.ContentEncryptionAlgorithms
                    .OrderBy(value => value)
                    .ToList();
            }
        }

        if (external is not null) {
            discovery.Document.RequestUriParameterSupported = true;
            discovery.Document.RequireRequestUriRegistration = true;
        }

        return AdviseResult.Continue;
    }
}
