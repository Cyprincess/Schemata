using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Validates create requests
///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso> on the wrap pipeline
///     by delegating to all registered <c>IValidationAdvisor&lt;TRequest&gt;</c> implementations.
///     When the request has <c>ValidateOnly = true</c>, throws
///     <c>NoContentException</c> after validation to signal a dry-run.
///     Suppressed when <see cref="CreateRequestValidationSuppressed" /> is present on the ambient
///     context or <see cref="SchemataResourceOptions.SuppressCreateValidation" /> is set.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceCreateValidationPipelineAdvisor<TEntity, TRequest, TDetail>
    : IRequestPipelineAdvisor<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<CreateResourceRequest<TEntity,TRequest,TDetail>,CreateResultBase<TDetail>> Members

    public int Order => SecurityOrders.Validation;

    public async Task<CreateResultBase<TDetail>> AdviseAsync(
        AdviceContext                                         ctx,
        CreateResourceRequest<TEntity, TRequest, TDetail>     request,
        RequestHandlerContinuation<CreateResultBase<TDetail>> next,
        CancellationToken                                     ct
    ) {
        var suppressed = ctx.Has<CreateRequestValidationSuppressed>()
                      || ctx.ServiceProvider.GetService<IOptions<SchemataResourceOptions>>()?.Value.SuppressCreateValidation == true;

        await ValidationHelper.ValidateAsync(ctx, request.Request, Operations.Create, suppressed, ct);

        return await next(ct);
    }

    #endregion
}

/// <summary>
///     Validates update requests
///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso> on the wrap pipeline
///     by delegating to all registered <c>IValidationAdvisor&lt;TRequest&gt;</c> implementations.
///     When the request has <c>ValidateOnly = true</c>, throws
///     <c>NoContentException</c> after validation to signal a dry-run.
///     Suppressed when <see cref="UpdateRequestValidationSuppressed" /> is present on the ambient
///     context or <see cref="SchemataResourceOptions.SuppressUpdateValidation" /> is set.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceUpdateValidationPipelineAdvisor<TEntity, TRequest, TDetail>
    : IRequestPipelineAdvisor<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<UpdateResourceRequest<TEntity,TRequest,TDetail>,UpdateResultBase<TDetail>> Members

    public int Order => SecurityOrders.Validation;

    public async Task<UpdateResultBase<TDetail>> AdviseAsync(
        AdviceContext                                         ctx,
        UpdateResourceRequest<TEntity, TRequest, TDetail>     request,
        RequestHandlerContinuation<UpdateResultBase<TDetail>> next,
        CancellationToken                                     ct
    ) {
        var suppressed = ctx.Has<UpdateRequestValidationSuppressed>()
                      || ctx.ServiceProvider.GetService<IOptions<SchemataResourceOptions>>()?.Value.SuppressUpdateValidation == true;

        await ValidationHelper.ValidateAsync(ctx, request.Request, Operations.Update, suppressed, ct);

        return await next(ct);
    }

    #endregion
}
