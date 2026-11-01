using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Core.Building;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Security.Skeleton;
using static Schemata.Security.Skeleton.AnonymousAccess;

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
    ///     The canonical name of the target resource. Used only when the registered method is
    ///     <see cref="ResourceMethodScope.Instance" />-scoped; a collection-scoped method ignores it.
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
    ///     <see cref="Schemata.Abstractions.Resource.ResourceMethodAttribute" />.
    /// </param>
    /// <param name="name">
    ///     The canonical name of the target resource. Used only when the registered method is
    ///     <see cref="ResourceMethodScope.Instance" />-scoped; a collection-scoped method ignores it.
    /// </param>
    /// <param name="request">The incoming request payload.</param>
    /// <param name="principal">The authenticated caller, or <see langword="null" /> for anonymous calls.</param>
    /// <param name="ctx">The advisor context shared with the caller.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The method's response.</returns>
    internal async Task<TResponse> InvokeCoreAsync(
        string            verb,
        string?           name,
        TRequest          request,
        ClaimsPrincipal?  principal,
        AdviceContext     ctx,
        CancellationToken ct
    ) {
        var scope = ResolveScope(verb);

        var container = new ResourceRequestContainer<TEntity>();
        var instance  = scope == ResourceMethodScope.Instance;
        if (instance) {
            // An instance-scoped method requires its target; ResourceIdentifiers.Apply rejects a
            // missing or malformed name before any business handler can run.
            ResourceIdentifiers.Apply(container, name);

            // The URI target identifies the resource for AIP-155 idempotency; carry it on the request
            // so the key distinguishes the same verb invoked against different resources.
            if (request is ICanonicalName canonical) {
                canonical.CanonicalName = name;
            }
        }

        var execution = new ResourceMethodExecution<TEntity, TRequest, TResponse> {
            Verb      = verb,
            Target    = instance ? name : null,
            Request   = request,
            Principal = principal,
        };

        // The idempotency wrap attaches its dispatch execution to the inner request DTO. Method
        // stages and the business handler report their transaction completion through it; a
        // dispatch whose owner never reports settles conservatively.
        execution.Dispatch = (request as IResourceDispatchCarrier)?.Dispatch;

        var requestResult = await HandledResponseRunner.RunAsync(
            execution,
            () => Advisor.For<IResourceMethodRequestAdvisor<TEntity, TRequest, TResponse>>()
                         .RunAsync(ctx, execution, container, ct),
            () => Blocked(name));
        if (requestResult is not null) {
            return requestResult;
        }

        if (instance) {
            var entity = await _repository.SingleOrDefaultAsync(
                q => container.Query(q), ct, new QueryOptions(IgnoreSoftDelete: true));

            if (entity is null) {
                // Entitlement-filtered null and physical absence are indistinguishable; authorize
                // the missing outcome before a NOT_FOUND, same matrix as the standard handlers.
                if (!IsAnonymous<TEntity>(verb)
                 && _sp.GetKeyedService<ResourceAccessStage>(typeof(TEntity)) is { } access) {
                    await access.FinalizeMissingAsync<TEntity, TRequest>(verb, request, name, principal, ct);
                }

                throw ResourceNotFound(name);
            }

            execution.Entity = entity;

            // The method stage starts with no handled response: a request-stage write that
            // continued must not stand in for the method stage's result.
            execution.ClearResponse();

            var methodResult = await HandledResponseRunner.RunAsync(
                execution,
                () => Advisor.For<IResourceMethodAdvisor<TEntity, TRequest, TResponse>>()
                             .RunAsync(ctx, execution, ct),
                () => Blocked(name));
            if (methodResult is not null) {
                return methodResult;
            }
        }

        // The business dispatch is running: without an explicit completion report the outcome
        // stays Reached, which the idempotency wrap treats as unknown and keeps pending.
        execution.Dispatch?.MarkReached();

        request.Principal = principal;
        return await _dispatcher.SendAsync<TRequest, TResponse>(request, ct);
    }

    private ResourceMethodScope ResolveScope(string verb) {
        var method = _sp.GetService<ResourceRegistry>()
                       ?.GetMethods(typeof(TEntity))
                        .FirstOrDefault(candidate => candidate.Verb == verb);
        if (method is null) {
            throw new InvalidOperationException(
                $"Custom method '{verb}' is not registered for resource '{typeof(TEntity).FullName}'; "
                + "register it on the resource before dispatch.");
        }

        return method.Scope;
    }

    private static NotFoundException Blocked(string? name) {
        return ResourceNotFound(name ?? ResourceNameDescriptor.ForType<TEntity>().Collection);
    }

    private static NotFoundException ResourceNotFound(string? name) {
        return SchemataResourceErrors.NotFound<TEntity>(name);
    }
}
