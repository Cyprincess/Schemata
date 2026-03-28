using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Handlers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceDiscoveryTokenExchange
{
    public const int DefaultOrder = AdviceDiscoveryRefreshToken.DefaultOrder + 10_000_000;
}

public sealed class AdviceDiscoveryTokenExchange<TApp>(IEnumerable<ITokenExchangeHandler<TApp>> handlers) : IDiscoveryAdvisor
    where TApp : SchemataApplication
{
    #region IDiscoveryAdvisor Members

    public int Order => AdviceDiscoveryTokenExchange.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DiscoveryContext  discovery,
        CancellationToken ct = default
    ) {
        if (!handlers.Any()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        discovery.Document                     ??= new();
        discovery.Document.GrantTypesSupported ??= [];
        discovery.Document.GrantTypesSupported.Add(GrantTypes.TokenExchange);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
