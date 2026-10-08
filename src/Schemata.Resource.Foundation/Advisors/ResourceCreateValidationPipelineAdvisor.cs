using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Validates create requests
///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso> on the wrap pipeline
///     by delegating to all registered <c>IValidationAdvisor&lt;TRequest&gt;</c> implementations.
///     Installed per resource unless the host excludes the stage with
///     <c>SchemataResourceBuilder.WithoutCreateValidation()</c>; a single operation can still skip
///     it with <see cref="CreateRequestValidationSuppressed" /> on the ambient context.
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
        var suppressed = ctx.Has<CreateRequestValidationSuppressed>();

        await ValidationHelper.ValidateAsync(ctx, request.Request, Operations.Create, suppressed, ct);

        return await next(ct);
    }

    #endregion
}