using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Publishes the <c>acr_values_supported</c> discovery metadata, per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderMetadata">
///         OpenID Connect Discovery 1.0
///         §3: OpenID Provider Metadata
///     </seealso>
///     : a JSON array of the Authentication Context Class References the OP supports, sourced
///     from <see cref="SchemataAuthorizationOptions.AcrValuesSupported" />.
/// </summary>
/// <seealso cref="AdviceDiscoveryDpop" />
public sealed class AdviceDiscoveryAcrValues(IOptions<SchemataAuthorizationOptions> options) : IDiscoveryAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDiscoveryDpop.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        if (options.Value.AcrValuesSupported.Count == 0) {
            return Task.FromResult(AdviseResult.Continue);
        }

        discovery.Document                  ??= new();
        discovery.Document.AcrValuesSupported = [..options.Value.AcrValuesSupported];

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
