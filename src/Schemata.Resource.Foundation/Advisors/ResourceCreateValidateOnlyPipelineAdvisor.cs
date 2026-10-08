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
///     Terminates a create request carrying <c>ValidateOnly = true</c>
///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>
///     with <see cref="NoContentException" /> before any persistence. Runs just after the
///     validation stage so a dry-run still reports validation errors when validation is
///     installed, and still terminates when the host excluded validation.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceCreateValidateOnlyPipelineAdvisor<TEntity, TRequest, TDetail>
    : IRequestPipelineAdvisor<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<CreateResourceRequest<TEntity,TRequest,TDetail>,CreateResultBase<TDetail>> Members

    public int Order => SecurityOrders.Validation + 1;

    public async Task<CreateResultBase<TDetail>> AdviseAsync(
        AdviceContext                                         ctx,
        CreateResourceRequest<TEntity, TRequest, TDetail>     request,
        RequestHandlerContinuation<CreateResultBase<TDetail>> next,
        CancellationToken                                     ct
    ) {
        if (request.Request is IValidation { ValidateOnly: true }) {
            throw new NoContentException();
        }

        return await next(ct);
    }

    #endregion
}
