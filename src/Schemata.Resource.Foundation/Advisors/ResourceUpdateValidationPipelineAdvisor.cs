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
///     Validates update requests
///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso> on the wrap pipeline
///     by delegating to all registered <c>IValidationAdvisor&lt;TRequest&gt;</c> implementations.
///     Installed per resource unless the host excludes the stage with
///     <c>SchemataResourceBuilder.WithoutUpdateValidation()</c>.
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
        await ValidationHelper.ValidateAsync(ctx, request.Request, Operations.Update, ct);

        return await next(ct);
    }

    #endregion
}