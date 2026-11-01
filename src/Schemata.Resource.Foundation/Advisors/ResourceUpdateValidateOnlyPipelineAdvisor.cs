using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Terminates an update request carrying <c>ValidateOnly = true</c>
///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>
///     with <see cref="NoContentException" /> before any persistence. Runs just after the
///     validation stage so a dry-run still reports validation errors when validation is
///     installed, and still terminates when the host excluded validation.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceUpdateValidateOnlyPipelineAdvisor<TEntity, TRequest, TDetail>
    : IRequestPipelineAdvisor<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<UpdateResourceRequest<TEntity,TRequest,TDetail>,UpdateResultBase<TDetail>> Members

    public int Order => SecurityOrders.Validation + 1;

    public async Task<UpdateResultBase<TDetail>> AdviseAsync(
        AdviceContext                                         ctx,
        UpdateResourceRequest<TEntity, TRequest, TDetail>     request,
        RequestHandlerContinuation<UpdateResultBase<TDetail>> next,
        CancellationToken                                     ct
    ) {
        if (request.Request is IValidation { ValidateOnly: true }) {
            throw new NoContentException();
        }

        return await next(ct);
    }

    #endregion
}
