using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Publishes the <c>dpop_signing_alg_values_supported</c> authorization server metadata,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-5.1">
///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
///         (DPoP) §5.1: Authorization Server Metadata
///     </seealso>
///     : a JSON array of the JWS alg values the authorization server supports for DPoP
///     proof JWTs, sourced from <see cref="DPopOptions.SigningAlgorithms" />.
/// </summary>
/// <remarks>
///     Registered only by the DPoP flow feature; a host that does not enable it advertises
///     no DPoP support.
/// </remarks>
/// <seealso cref="AdviceDiscoveryClientAuthentication" />
public sealed class AdviceDiscoveryDpop(IOptions<DPopOptions> options) : IDiscoveryAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDiscoveryBackChannelLogout.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        if (options.Value.SigningAlgorithms.Count == 0) {
            return Task.FromResult(AdviseResult.Continue);
        }

        discovery.Document                               ??= new();
        discovery.Document.DpopSigningAlgValuesSupported =   [..options.Value.SigningAlgorithms];

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
