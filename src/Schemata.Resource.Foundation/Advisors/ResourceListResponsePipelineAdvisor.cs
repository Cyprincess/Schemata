using System;
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
///     Derives <see cref="IChild.Parent" /> on each list summary from the summary's own
///     <see cref="ICanonicalName.CanonicalName" />, on the wrap pipeline after the handler
///     builds the response.
/// </summary>
/// <remarks>
///     The advisor fires only when <typeparamref name="TSummary" /> implements <see cref="IChild" />.
///     Each element is mutated in place — the collection on the response is not replaced.
/// </remarks>
/// <typeparam name="TEntity">The entity type being listed.</typeparam>
/// <typeparam name="TSummary">The summary DTO type.</typeparam>
public sealed class ResourceListResponsePipelineAdvisor<TEntity, TSummary>
    : IRequestPipelineAdvisor<ListResourceQueryRequest<TEntity, TSummary>, ListResultBase<TSummary>>
    where TEntity : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<ListResourceQueryRequest<TEntity,TSummary>,ListResultBase<TSummary>> Members

    public int Order => SecurityOrders.ResponseFamily;

    public async Task<ListResultBase<TSummary>> AdviseAsync(
        AdviceContext                                        ctx,
        ListResourceQueryRequest<TEntity, TSummary>          request,
        RequestHandlerContinuation<ListResultBase<TSummary>> next,
        CancellationToken                                    ct
    ) {
        var response = await next(ct);

        if (!typeof(IChild).IsAssignableFrom(typeof(TSummary)) || response.Entities is null or { Count: 0 }) {
            return response;
        }

        foreach (var summary in response.Entities) {
            if (summary is not IChild child) {
                continue;
            }

            var parent = ChildParentHelper.DeriveParent(summary.CanonicalName);
            if (!string.Equals(child.Parent, parent, StringComparison.Ordinal)) {
                child.Parent = parent;
            }
        }

        return response;
    }

    #endregion
}
