using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceDiscoveryNativeSso : IDiscoveryAdvisor
{
    public const int DefaultOrder = 900_000_000 - 20_000_000;

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        discovery.Document                   ??= new();
        discovery.Document.NativeSsoSupported = true;
        return Task.FromResult(AdviseResult.Continue);
    }
}