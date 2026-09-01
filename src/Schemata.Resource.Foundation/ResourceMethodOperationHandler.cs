using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Internal;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Transport facade for an AIP-136 custom method: wraps the verb, instance target, payload, and
///     caller into a <see cref="ResourceMethodRequest{TEntity,TRequest,TResponse}" /> and hands it to
///     the <see cref="IRequestDispatcher" />, whose registered
///     <see cref="Handlers.ResourceMethodDispatchHandler{TEntity,TRequest,TResponse}" /> runs the
///     resource advisor pipeline below the wrap-position advisors.
/// </summary>
/// <typeparam name="TEntity">The resource entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodOperationHandler<TEntity, TRequest, TResponse>(
    IRepository<TEntity> repository,
    IServiceProvider     sp,
    IRequestDispatcher   dispatcher
)
    where TEntity : class, ICanonicalName
    where TRequest : class, IRequest<TResponse>, IRequestPrincipal
    where TResponse : class, ICanonicalName
{
    private readonly IRequestDispatcher   _dispatcher = dispatcher;
    private readonly IRepository<TEntity> _repository = repository;
    private readonly IServiceProvider     _sp         = sp;

    /// <summary>
    ///     Dispatches the custom method's verb envelope through the unified request pipeline; the
    ///     registered dispatch handler runs the resource advisor pipeline and the inner command
    ///     dispatch.
    /// </summary>
    /// <param name="verb">
    ///     The verb in lowerCamelCase as declared by
    ///     <see cref="Schemata.Abstractions.Resource.ResourceMethodAttribute" />.
    /// </param>
    /// <param name="name">
    ///     The canonical name of the target resource for
    ///     <see cref="ResourceMethodScope.Instance" />-scoped methods, or
    ///     <see langword="null" /> for
    ///     <see cref="ResourceMethodScope.Collection" />-scoped methods.
    /// </param>
    /// <param name="request">The incoming request payload.</param>
    /// <param name="principal">
    ///     The authenticated caller, or
    ///     <see langword="null" /> for anonymous calls.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The method's response.</returns>
    /// <exception cref="NotFoundException">
    ///     An advisor stage blocked the invocation.
    /// </exception>
    public Task<TResponse> InvokeAsync(
        string            verb,
        string?           name,
        TRequest          request,
        ClaimsPrincipal?  principal,
        CancellationToken? ct
    ) {
        return _dispatcher.SendAsync<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>(
            new(verb, name, request, principal), ct ?? CancellationToken.None);
    }

    /// <summary>
    ///     Runs the full custom-method resource advisor pipeline, then dispatches the request
    ///     through the command or query pipeline.
    /// </summary>
    /// <param name="verb">
    ///     The verb in lowerCamelCase as declared by
    ///     <see cref="Schemata.Abstractions.Resource.ResourceMethodAttribute" />. Stashed in the
    ///     <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> as
    ///     <see cref="ResourceMethodVerb" /> so the legacy method request advisors can key on it.
    /// </param>
    /// <param name="name">
    ///     The canonical name of the target resource for instance-scoped methods, or
    ///     <see langword="null" /> for collection-scoped methods.
    /// </param>
    /// <param name="request">The incoming request payload.</param>
    /// <param name="principal">The authenticated caller, or <see langword="null" /> for anonymous calls.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The method's response.</returns>
    internal async Task<TResponse> InvokeCoreAsync(
        string            verb,
        string?           name,
        TRequest          request,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    ) {
        var ctx = ResourceAdviceContext.Create(_sp);
        ctx.Set(new ResourceMethodVerb(verb));

        var container = new ResourceRequestContainer<TEntity>();
        if (name is not null) {
            ResourceIdentifiers.Apply(container, name);

            // The URI target identifies the resource for AIP-155 idempotency; carry it on the request
            // so the key distinguishes the same verb invoked against different resources.
            if (request is ICanonicalName canonical) {
                canonical.CanonicalName = name;
            }
        }

        var requestResult = await ResourcePipelineRunner<string>.RunAsync<TResponse>(
            ctx,
            () => Advisor.For<IResourceMethodRequestAdvisor<TEntity, TRequest>>()
                         .RunAsync(ctx, request, container, principal, ct), () => Blocked(name));
        if (requestResult is not null) {
            return requestResult;
        }

        TEntity? entity = null;
        if (name is not null) {
            using (_repository.SuppressQuerySoftDelete()) {
                entity = await _repository.SingleOrDefaultAsync(q => container.Query(q), ct);
            }

            if (entity is null) {
                throw ResourceNotFound(name);
            }

            var methodResult = await ResourcePipelineRunner<string>.RunAsync<TResponse>(
                ctx,
                () => Advisor.For<IResourceMethodAdvisor<TEntity, TRequest, TResponse>>()
                             .RunAsync(ctx, request, entity, principal, ct), () => Blocked(name));
            if (methodResult is not null) {
                return methodResult;
            }
        }

        request.Principal = principal;
        return await _dispatcher.SendAsync<TRequest, TResponse>(request, ct);
    }

    private static NotFoundException Blocked(string? name) {
        return ResourceNotFound(name ?? ResourceNameDescriptor.ForType<TEntity>().Collection);
    }

    private static NotFoundException ResourceNotFound(string? name) {
        return SchemataResourceErrors.NotFound<TEntity>(name);
    }
}
