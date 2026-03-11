using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

internal sealed record PendingIdempotencyKey(string RequestId);

public sealed class AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private readonly IIdempotencyStore _store;

    public AdviceCreateRequestIdempotency(IIdempotencyStore store) { _store = store; }

    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    public int Order => 50_000_000;

    public int Priority => Order;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (request is not IRequestIdentification { RequestId: { } requestId }) {
            return AdviseResult.Continue;
        }

        if (ctx.Has<SuppressCreateIdempotency>()) {
            return AdviseResult.Continue;
        }

        var cached = await _store.GetAsync<CreateResult<TDetail>>(requestId, ct);
        if (cached is not null) {
            ctx.Set(cached);
            return AdviseResult.Handle;
        }

        ctx.Set(new PendingIdempotencyKey(requestId));
        return AdviseResult.Continue;
    }

    #endregion
}
