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
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Internal;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Coordinates the advisor pipeline for an AIP-136 custom method invocation:
///     <see cref="IResourceRequestAdvisor{TEntity}" /> gate
///     -> <see cref="IResourceMethodRequestAdvisor{TEntity, TRequest}" /> request stage
///     -> <see cref="IResourceMethodAdvisor{TEntity, TRequest, TResponse}" /> method stage
///     -> registered <see cref="IResourceMethodHandler{TEntity,TRequest,TResponse}" />
///     -> <see cref="IResourceResponseAdvisor{TEntity, TResponse}" /> response stage.
///     Each stage may short-circuit by stashing a <typeparamref name="TResponse" />
///     in the <see cref="AdviceContext" /> and returning
///     <see cref="AdviseResult.Handle" />.
/// </summary>
/// <typeparam name="TEntity">The resource entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodOperationHandler<TEntity, TRequest, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class
    where TResponse : class, ICanonicalName
{
    private readonly IRepository<TEntity> _repository;
    private readonly IServiceProvider     _sp;

    /// <summary>
    ///     Initializes the custom-method operation handler.
    /// </summary>
    /// <param name="repository">The repository for loading instance-scoped resources.</param>
    /// <param name="sp">The service provider for resolving advisors and options.</param>
    public ResourceMethodOperationHandler(IRepository<TEntity> repository, IServiceProvider sp) {
        _repository = repository;
        _sp         = sp;
    }

    /// <summary>
    ///     Runs the full custom-method advisor pipeline around the registered
    ///     handler.
    /// </summary>
    /// <param name="handler">
    ///     The resolved handler implementing
    ///     <see cref="IResourceMethodHandler{TEntity, TRequest, TResponse}" />.
    /// </param>
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
    public async Task<TResponse> InvokeAsync(
        IResourceMethodHandler<TEntity, TRequest, TResponse> handler,
        string                                               verb,
        string?                                              name,
        TRequest                                             request,
        ClaimsPrincipal?                                     principal,
        CancellationToken?                                   ct
    ) {
        ct ??= CancellationToken.None;

        var ctx = ResourceAdviceContext.Create(_sp);
        ctx.Set(new ResourceMethodVerb(verb));

        return await InvokeCoreAsync(ctx, handler, verb, name, request, principal, ct.Value);
    }

    private async Task<TResponse> InvokeCoreAsync(
        AdviceContext                                        ctx,
        IResourceMethodHandler<TEntity, TRequest, TResponse> handler,
        string                                               verb,
        string?                                              name,
        TRequest                                             request,
        ClaimsPrincipal?                                     principal,
        CancellationToken                                    ct
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

        var response = await handler.InvokeAsync(name, request, entity, principal, ct);

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
