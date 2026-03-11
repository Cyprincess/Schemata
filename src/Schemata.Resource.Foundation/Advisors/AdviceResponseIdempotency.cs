using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class AdviceResponseIdempotency<TEntity, TDetail> : IResourceResponseAdvisor<TEntity, TDetail>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private readonly IIdempotencyStore _store;

    public AdviceResponseIdempotency(IIdempotencyStore store) { _store = store; }

    #region IResourceResponseAdvisor<TEntity,TDetail> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TEntity?          entity,
        TDetail?          detail,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (!ctx.TryGet<PendingIdempotencyKey>(out var pending) || pending is null) {
            return AdviseResult.Continue;
        }

        if (detail is null) {
            return AdviseResult.Continue;
        }

        await _store.SetAsync(pending.RequestId, new CreateResult<TDetail> { Detail = detail }, ct: ct);
        return AdviseResult.Continue;
    }

    #endregion
}
