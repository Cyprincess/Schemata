using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds the <c>end_session_endpoint</c> to the discovery document,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">OpenID Connect RP-Initiated Logout 1.0</seealso>
///     .
/// </summary>
/// <seealso cref="AdviceDiscoveryRevocation" />
public sealed class AdviceDiscoveryEndSession : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryRevocation.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        var issuer = discovery.Issuer;

        discovery.Document                    ??= new();
        discovery.Document.EndSessionEndpoint =   $"{issuer}{Endpoints.EndSession}";

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
