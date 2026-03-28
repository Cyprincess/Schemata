using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceListRequestAnonymous
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceListRequestAnonymous<TEntity> : IResourceListRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    #region IResourceListRequestAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceListRequestAnonymous.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        ListRequest                       request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.List))) {
            ctx.Set(new AnonymousGranted());
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
