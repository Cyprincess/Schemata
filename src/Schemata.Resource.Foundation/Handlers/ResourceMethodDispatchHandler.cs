using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;

namespace Schemata.Resource.Foundation.Handlers;

/// <summary>
///     Unwraps a dispatched <see cref="ResourceMethodRequest{TEntity,TRequest,TResponse}" /> and runs
///     the resource advisor pipeline, instance loading, and inner dispatch of the custom method.
/// </summary>
/// <typeparam name="TEntity">The resource entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodDispatchHandler<TEntity, TRequest, TResponse>(
    ResourceMethodOperationHandler<TEntity, TRequest, TResponse> operation
) : IRequestHandler<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class, IRequest<TResponse>, IRequestPrincipal
    where TResponse : class, ICanonicalName
{
    #region IRequestHandler<ResourceMethodRequest<TEntity,TRequest,TResponse>,TResponse> Members

    public Task<TResponse> HandleAsync(ResourceMethodRequest<TEntity, TRequest, TResponse> request, CancellationToken ct = default) {
        return operation.InvokeCoreAsync(request.Verb, request.Name, request.Request, request.Principal, ct);
    }

    #endregion
}
