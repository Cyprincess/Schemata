using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Commands;
using Schemata.Abstractions.Advisors;

namespace Schemata.Resource.Foundation.Handlers;

internal sealed class DefaultCreateResourceHandler<TEntity, TRequest, TDetail, TSummary>(
    ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary> operation
) : IRequestHandler<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    public Task<CreateResultBase<TDetail>> HandleAsync(
        CreateResourceRequest<TEntity, TRequest, TDetail> request,
        AdviceContext                                     context,
        CancellationToken                                 ct = default
    ) {
        return operation.CreateAsync(request.Request, request.Principal, context, ct, request.Dispatch);
    }
}
