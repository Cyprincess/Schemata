using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Adds front-channel logout capabilities to the discovery document, per
///     <seealso href="https://openid.net/specs/openid-connect-frontchannel-1_0.html">
///         OpenID Connect Front-Channel Logout
///         1.0
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     Sets <c>frontchannel_logout_supported</c> and <c>frontchannel_logout_session_supported</c> to <c>true</c>.
/// </remarks>
/// <seealso cref="AdviceDiscoveryBackChannelLogout" />
public sealed class AdviceDiscoveryFrontChannelLogout : IDiscoveryAdvisor
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
        discovery.Document                                    ??= new();
        discovery.Document.FrontchannelLogoutSupported        =   true;
        discovery.Document.FrontchannelLogoutSessionSupported =   true;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
