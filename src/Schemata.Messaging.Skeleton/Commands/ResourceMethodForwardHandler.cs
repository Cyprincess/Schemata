using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Messaging.Skeleton.Commands;

/// <summary>
///     Unwraps a <see cref="ResourceMethodRequest{TEntity,TRequest,TResponse}" /> for a domain whose
///     method command runs its own handler pipeline: forwards the envelope's principal onto the inner
///     request and dispatches it through the <see cref="IRequestDispatcher" />, so the original
///     handler and its registered command advisors run exactly once per envelope. Resource methods
///     that need the resource advisor pipeline register
///     <c>ResourceMethodDispatchHandler</c> (Schemata.Resource.Foundation) instead.
/// </summary>
/// <typeparam name="TEntity">The resource entity type the method belongs to.</typeparam>
/// <typeparam name="TRequest">The method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The method's response type.</typeparam>
public sealed class ResourceMethodForwardHandler<TEntity, TRequest, TResponse>(IRequestDispatcher dispatcher)
    : IRequestHandler<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class, IRequest<TResponse>
    where TResponse : class
{
    #region IRequestHandler<ResourceMethodRequest<TEntity,TRequest,TResponse>,TResponse> Members

    public Task<TResponse> HandleAsync(ResourceMethodRequest<TEntity, TRequest, TResponse> request, CancellationToken ct = default) {
        if (request.Request is IRequestPrincipal principal) {
            principal.Principal = request.Principal;
        }

        return dispatcher.SendAsync<TRequest, TResponse>(request.Request, ct);
    }

    #endregion
}
