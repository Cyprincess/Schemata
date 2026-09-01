using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Common.Errors;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Internal;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Runs the AIP-136 resource advisor pipeline, validates an instance target, and dispatches the
///     resulting request through the unified request pipeline. Each resource-advisor stage may
///     short-circuit with a response stored in its
///     <see cref="AdviceContext" />.
/// </summary>
/// <typeparam name="TEntity">The resource entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodOperationHandler<TEntity, TRequest, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class, IRequest<TResponse>, IRequestPrincipal
    where TResponse : class, ICanonicalName
{
    private readonly IRequestDispatcher   _dispatcher;
    private readonly IRepository<TEntity> _repository;
    private readonly IServiceProvider     _sp;

    /// <summary>Initializes the custom-method operation pipeline.</summary>
    public ResourceMethodOperationHandler(
        IRepository<TEntity> repository,
        IServiceProvider     sp,
        IRequestDispatcher   dispatcher
    ) {
        _repository = repository;
        _sp         = sp;
        _dispatcher = dispatcher;
    }

    /// <summary>
    ///     Runs the full custom-method resource advisor pipeline, then dispatches the request
    ///     through the command or query pipeline.
    /// </summary>
    /// <param name="verb">
    ///     The verb in lowerCamelCase as declared by
    ///     <see cref="Schemata.Abstractions.Resource.ResourceMethodAttribute" />.
    ///     Stashed in the <see cref="AdviceContext" /> as
    ///     <see cref="ResourceMethodVerb" /> so downstream advisors can key on it,
    ///     and used as the operation token passed to the
    ///     <see cref="IResourceRequestAdvisor{TEntity}" /> gate.
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
    /// <remarks>
    ///     The resource pipeline is the transport-facing root for authorization and target
    ///     validation. Dispatch then runs command or query advisors and the single request
    ///     handler with the same principal supplied by HTTP or gRPC.
    /// </remarks>
    public async Task<TResponse> InvokeAsync(
        string           verb,
        string?          name,
        TRequest         request,
        ClaimsPrincipal? principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        using var scope = AdviceContext.Current is null ? AdviceContext.Establish(new AdviceContext(_sp)) : null;

        var ctx = ResourceAdviceContext.Create(_sp);
        ctx.Set(new ResourceMethodVerb(verb));

        return await InvokeCoreAsync(ctx, verb, name, request, principal, ct.Value);
    }

    private async Task<TResponse> InvokeCoreAsync(
        AdviceContext    ctx,
        string           verb,
        string?          name,
        TRequest         request,
        ClaimsPrincipal? principal,
        CancellationToken ct
    ) {
        var gate = await ResourcePipelineRunner<string>.RunAsync<TResponse>(
            ctx, () => Advisor.For<IResourceRequestAdvisor<TEntity>>().RunAsync(ctx, principal, verb, ct),
            () => Blocked(name));
        if (gate is not null) {
            return gate;
        }

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
        var response = await _dispatcher.SendAsync<TRequest, TResponse>(request, ct);

        var responseResult = await ResourcePipelineRunner<string>.RunAsync<TResponse>(
            ctx,
            () => Advisor.For<IResourceResponseAdvisor<TEntity, TResponse>>()
                         .RunAsync(ctx, null, response, principal, ct), () => Blocked(name));
        return responseResult ?? response;
    }

    private static NotFoundException Blocked(string? name) {
        return ResourceNotFound(name ?? ResourceNameDescriptor.ForType<TEntity>().Collection);
    }

    private static NotFoundException ResourceNotFound(string? name) {
        return SchemataResourceErrors.NotFound<TEntity>(name);
    }
}
