using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Handlers;

internal sealed class DefaultGetResourceHandler<TEntity, TRequest, TDetail, TSummary>(
    ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> operation
) : IRequestHandler<GetResourceQueryRequest<TEntity, TDetail>, GetResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    public Task<GetResultBase<TDetail>> HandleAsync(
        GetResourceQueryRequest<TEntity, TDetail> request,
        CancellationToken                ct = default
    ) {
        return operation.GetAsync(request.Request, request.Principal, ct);
    }
}
