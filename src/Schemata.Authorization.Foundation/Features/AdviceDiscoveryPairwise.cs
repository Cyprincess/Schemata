using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Appends <c>pairwise</c> to the discovery <c>subject_types_supported</c> metadata when
///     <see cref="SchemataAuthorizationOptions.PairwiseSalt" /> is configured, per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderMetadata">
///         OpenID Connect Discovery 1.0 §3: Provider Configuration Metadata
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     Registered only by the pairwise flow feature; the salt configures the feature, it does not
///     stand in for it — without installation the document stays <c>public</c>-only.
/// </remarks>
public sealed class AdviceDiscoveryPairwise(IOptions<SchemataAuthorizationOptions> options) : IDiscoveryAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDiscoveryRichAuthorization.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        if (string.IsNullOrWhiteSpace(options.Value.PairwiseSalt)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        discovery.Document                       ??= new();
        discovery.Document.SubjectTypesSupported ??= [AuthorizationConstants.SubjectTypes.Public];
        if (!discovery.Document.SubjectTypesSupported.Contains(AuthorizationConstants.SubjectTypes.Pairwise)) {
            discovery.Document.SubjectTypesSupported.Add(AuthorizationConstants.SubjectTypes.Pairwise);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}