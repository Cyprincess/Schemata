using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Entity.Repository;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Coordinates the advisor pipeline for an AIP-136 custom method invocation:
///     <see cref="IResourceRequestAdvisor{TEntity}" /> gate
///         -> <see cref="IResourceMethodRequestAdvisor{TEntity, TRequest}" /> request stage
///         -> <see cref="IResourceMethodAdvisor{TEntity, TRequest, TResponse}" /> method stage
///         -> registered <see cref="IResourceMethodHandler{TEntity, TRequest, TResponse}" />
///         -> <see cref="IResourceResponseAdvisor{TEntity, TResponse}" /> response stage.
///     Each stage may short-circuit by stashing a <typeparamref name="TResponse" />
///     in the <see cref="AdviceContext" /> and returning
///     <see cref="AdviseResult.Handle" />.
/// </summary>
/// <typeparam name="TEntity">The resource entity type.</typeparam>
/// <typeparam name="TRequest">The custom method's request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class ResourceMethodOperationHandler<TEntity, TRequest, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TResponse : class, ICanonicalName
{
    private readonly IRepository<TEntity> _repository;
    private readonly IServiceProvider     _sp;

    public ResourceMethodOperationHandler(IRepository<TEntity> repository, IServiceProvider sp) {
        _repository = repository;
        _sp         = sp;
    }

    /// <summary>
    ///     Runs the full custom-method advisor pipeline around the registered
    ///     handler.
    /// </summary>
    /// <param name="handler">The resolved handler implementing
    ///     <see cref="IResourceMethodHandler{TEntity, TRequest, TResponse}" />.</param>
    /// <param name="verb">The verb in lowerCamelCase as declared by
    ///     <see cref="Schemata.Abstractions.Resource.ResourceMethodAttribute" />.
    ///     Stashed in the <see cref="AdviceContext" /> as
    ///     <see cref="ResourceMethodVerb" /> so downstream advisors can key on it,
    ///     and used as the operation token passed to the
    ///     <see cref="IResourceRequestAdvisor{TEntity}" /> gate.</param>
    /// <param name="name">The canonical name of the target resource for
    ///     <see cref="ResourceMethodScope.Instance" />-scoped methods, or
    ///     <see langword="null" /> for
    ///     <see cref="ResourceMethodScope.Collection" />-scoped methods.</param>
    /// <param name="request">The incoming request payload.</param>
    /// <param name="principal">The authenticated caller, or
    ///     <see langword="null" /> for anonymous calls.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The method's response, or <see langword="null" /> when any
    ///     advisor stage blocks.</returns>
    public async Task<TResponse?> InvokeAsync(
        IResourceMethodHandler<TEntity, TRequest, TResponse> handler,
        string                                               verb,
        string?                                              name,
        TRequest                                             request,
        ClaimsPrincipal?                                     principal,
        CancellationToken?                                   ct
    ) {
        ct ??= CancellationToken.None;

        var ctx = new AdviceContext(_sp);
        ctx.Set(new ResourceMethodVerb(verb));

        switch (await Advisor.For<IResourceRequestAdvisor<TEntity>>()
                             .RunAsync(ctx, principal, verb, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<TResponse>(out var pre):
                return pre;
            case AdviseResult.Block:
            default:
                return null;
        }

        var container = new ResourceRequestContainer<TEntity>();

        switch (await Advisor.For<IResourceMethodRequestAdvisor<TEntity, TRequest>>()
                             .RunAsync(ctx, request, container, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<TResponse>(out var pre):
                return pre;
            case AdviseResult.Block:
            default:
                return null;
        }

        TEntity? entity;
        using (_repository.SuppressQuerySoftDelete()) {
            entity = await _repository.SingleOrDefaultAsync(q => container.Query(q), ct.Value);
        }
        if (entity == null) {
            throw ResourceOperationHandler<TEntity, TRequest, TResponse, TResponse>.ResourceNotFound(name);
        }

        switch (await Advisor.For<IResourceMethodAdvisor<TEntity, TRequest, TResponse>>()
                             .RunAsync(ctx, request, entity, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<TResponse>(out var pre):
                return pre;
            case AdviseResult.Block:
            default:
                return null;
        }

        var response = await handler.InvokeAsync(name, request, entity, principal, ct.Value);

        switch (await Advisor.For<IResourceResponseAdvisor<TEntity, TResponse>>()
                             .RunAsync(ctx, null, response, principal, ct.Value)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<TResponse>(out var pre):
                return pre;
            case AdviseResult.Block:
            default:
                return null;
        }

        return response;
    }
}
