using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Handlers;

internal sealed class DefaultListResourceHandler<TEntity, TRequest, TDetail, TSummary>(
    ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> operation
) : IRequestHandler<ListResourceQueryRequest<TEntity, TSummary>, ListResultBase<TSummary>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    public Task<ListResultBase<TSummary>> HandleAsync(
        ListResourceQueryRequest<TEntity, TSummary> request,
        CancellationToken                  ct = default
    ) {
        return operation.ListAsync(request.Request, request.Principal, ct);
    }
}
