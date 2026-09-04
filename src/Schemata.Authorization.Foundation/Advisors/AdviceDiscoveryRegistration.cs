using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the <c>registration_endpoint</c> to the discovery document,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html">
///         OpenID Connect Discovery 1.0 §3: Provider Metadata
///     </seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryIntrospection" />
public sealed class AdviceDiscoveryRegistration : IDiscoveryAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceDiscoveryIntrospection.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        discovery.Document ??= new();
        discovery.Document.RegistrationEndpoint = $"{discovery.Issuer}{Endpoints.Register}";

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
