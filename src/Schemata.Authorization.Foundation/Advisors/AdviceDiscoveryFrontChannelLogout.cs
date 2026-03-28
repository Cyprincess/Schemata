using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceDiscoveryFrontChannelLogout : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryRevocation.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

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
