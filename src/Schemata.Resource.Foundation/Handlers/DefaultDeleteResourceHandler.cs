using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;

namespace Schemata.Resource.Foundation.Handlers;

internal sealed class DefaultDeleteResourceHandler<TEntity, TRequest, TDetail, TSummary>(
    ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> operation
) : IRequestHandler<DeleteResourceRequest<TEntity, TDetail>, DeleteResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    public Task<DeleteResultBase<TDetail>> HandleAsync(
        DeleteResourceRequest<TEntity, TDetail> request,
        CancellationToken              ct = default
    ) {
        return operation.DeleteAsync(request.Name, request.Etag, request.Principal, ct, request.AllowMissing);
    }
}
