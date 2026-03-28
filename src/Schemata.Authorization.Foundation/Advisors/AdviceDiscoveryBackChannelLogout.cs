using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceDiscoveryBackChannelLogout : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryFrontChannelLogout.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        discovery.Document                                   ??= new();
        discovery.Document.BackchannelLogoutSupported        =   true;
        discovery.Document.BackchannelLogoutSessionSupported =   true;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
