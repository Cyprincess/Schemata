using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Clears server-managed fields on an Update request on the wrap pipeline and strips matching
///     paths from the update mask. Mask stripping prevents clients from clearing fields such as
///     <c>owner</c> by setting <c>update_mask=owner</c> after the payload field is ignored.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying update data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceUpdateSanitizePipelineAdvisor<TEntity, TRequest, TDetail>
    : IRequestPipelineAdvisor<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<UpdateResourceRequest<TEntity,TRequest,TDetail>,UpdateResultBase<TDetail>> Members

    public int Order => SecurityOrders.Sanitize;

    public Task<UpdateResultBase<TDetail>> AdviseAsync(
        AdviceContext                                         ctx,
        UpdateResourceRequest<TEntity, TRequest, TDetail>     request,
        RequestHandlerContinuation<UpdateResultBase<TDetail>> next,
        CancellationToken                                     ct
    ) {
        ResourceSanitizePipelineAdvisor.ClearSystemFields(request.Request, ResourceSanitizePipelineAdvisor.UpdateSystemFields);

        if (request.Request is IUpdateMask { UpdateMask: { } mask } mut) {
            var remaining = mask.Split(',')
                                .Select(f => f.Trim())
                                .Where(f => f.Length != 0 && !ResourceSanitizePipelineAdvisor.UpdateSystemFields.Contains(ResourceWireNameRules.ResolveClrName(typeof(TRequest), f.Split('.')[0])));

            mut.UpdateMask = string.Join(",", remaining);
        }

        return next(ct);
    }

    #endregion
}