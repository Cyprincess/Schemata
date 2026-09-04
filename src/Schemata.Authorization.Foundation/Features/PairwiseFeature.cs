using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Features;

/// <summary>
///     Offers pairwise subject identifiers as an opt-in flow feature, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#SubjectIDTypes">
///         OpenID Connect Core 1.0 §8: Subject Identifier Types
///     </seealso>
///     . Everything pairwise is registered here: the claims advisor that projects <c>sub</c> for
///     applications whose subject type is <c>pairwise</c>, the persistent canonical ⇄ pairwise
///     <see cref="PairwiseSubjectTranslator{TApp}" />, and the discovery advisor advertising
///     <c>pairwise</c> in <c>subject_types_supported</c>. DI presence is the switch: a host that
///     does not install the feature serves public subjects only, regardless of
///     <see cref="SchemataAuthorizationOptions.SubjectType" /> or
///     <see cref="SchemataAuthorizationOptions.PairwiseSalt" />.
/// </summary>
/// <typeparam name="TApp">The configured application entity type.</typeparam>
/// <remarks>
///     Installed via <c>UsePairwiseSubjects()</c> on
///     <see cref="SchemataAuthorizationBuilder{TApp, TAuth, TScope}" />.
/// </remarks>
public sealed class PairwiseFeature<TApp> : IAuthorizationFlowFeature
    where TApp : SchemataApplication
{
    #region IAuthorizationFlowFeature Members

    public int Order => PairwiseFeature.DefaultOrder;

    public void ConfigureServices(IServiceCollection services, SchemataOptions schemata, Configurators configurators) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IClaimsAdvisor, AdviceClaimsPairwise<TApp>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDiscoveryAdvisor, AdviceDiscoveryPairwise>());

        // Pairwise (application × canonical-subject) → pairwise-hash mapping lives in
        // SchemataSubjectMapping. The hosting startup must register a repository for that
        // entity against its DbContext so AdviceClaimsPairwise (writer) and
        // IPairwiseSubjectTranslator (reader / reverse-lookup) share the same durable table.
        services.TryAddScoped<PairwiseSubjectTranslator<TApp>>();
        services.TryAddScoped<IPairwiseSubjectTranslator>(
            sp => sp.GetRequiredService<PairwiseSubjectTranslator<TApp>>());
    }

    #endregion
}

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
        discovery.Document.SubjectTypesSupported ??= [SubjectTypes.Public];
        if (!discovery.Document.SubjectTypesSupported.Contains(SubjectTypes.Pairwise)) {
            discovery.Document.SubjectTypesSupported.Add(SubjectTypes.Pairwise);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}


/// <summary>
///     Ordering anchor for <see cref="PairwiseFeature{TApp}" /> so successor features can chain
///     off its <c>DefaultOrder</c> without naming type arguments.
/// </summary>
internal static class PairwiseFeature
{
    /// <summary>The default feature ordering value (chained after its predecessor).</summary>
    public const int DefaultOrder = RichAuthorizationFeature.DefaultOrder + 100;
}
