using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Handlers;

internal sealed class DefaultUpdateResourceHandler<TEntity, TRequest, TDetail, TSummary>(
    ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> operation
) : IRequestHandler<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    public Task<UpdateResultBase<TDetail>> HandleAsync(
        UpdateResourceRequest<TEntity, TRequest, TDetail> request,
        CancellationToken                       ct = default
    ) {
        return operation.UpdateAsync(request.Name, request.Request, request.Principal, ct);
    }
}
