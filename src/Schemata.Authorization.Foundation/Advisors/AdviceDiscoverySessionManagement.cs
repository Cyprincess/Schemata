using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceDiscoverySessionManagement : IDiscoveryAdvisor
{
    public const int DefaultOrder = 900_000_000 - 10_000_000;

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        discovery.Document                ??= new();
        discovery.Document.CheckSessionIframe = $"{discovery.Issuer}{Endpoints.CheckSession}";
        return Task.FromResult(AdviseResult.Continue);
    }
}