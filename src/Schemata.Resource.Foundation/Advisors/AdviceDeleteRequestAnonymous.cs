using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceDeleteRequestAnonymous
{
    public const int DefaultOrder = Orders.Base;
}

public sealed class AdviceDeleteRequestAnonymous<TEntity> : IResourceDeleteRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    #region IResourceDeleteRequestAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceDeleteRequestAnonymous.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        DeleteRequest                     request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Delete))) {
            ctx.Set(new AnonymousGranted());
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
