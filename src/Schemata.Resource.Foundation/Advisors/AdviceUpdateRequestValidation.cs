using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class AdviceUpdateRequestValidation<TEntity, TRequest> : IResourceUpdateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceUpdateRequestAdvisor<TEntity,TRequest> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        return ValidationHelper.ValidateAsync(ctx, request, Operations.Update, ctx.Has<SuppressUpdateRequestValidation>(), ct);
    }

    #endregion
}
