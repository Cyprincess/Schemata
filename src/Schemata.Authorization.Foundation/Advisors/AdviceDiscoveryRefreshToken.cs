using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public sealed class AdviceDiscoveryRefreshToken : IDiscoveryAdvisor
{
    public const int DefaultOrder = AdviceDiscoveryClientCredentials.DefaultOrder + 10_000_000;

    #region IDiscoveryAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        discovery.Document                     ??= new();
        discovery.Document.GrantTypesSupported ??= [];
        discovery.Document.GrantTypesSupported.Add(GrantTypes.RefreshToken);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
