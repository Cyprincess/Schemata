using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Order constants and system-managed wire fields for
///     <see cref="AdviceUpdateRequestSanitize{TEntity, TRequest}" />.
/// </summary>
public static class AdviceUpdateRequestSanitize
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceUpdateRequestAuthorize{TEntity,TRequest}" />
    ///     so authorization decisions are made against the unaltered client payload.
    /// </summary>
    public const int DefaultOrder = AdviceUpdateRequestAuthorize.DefaultOrder + 10_000_000;
    
    /// <summary>
    ///     CLR property names of fields that clients MUST NOT populate on a Create request. The server
    ///     either assigns them (name/canonical_name/uid/owner/etag/timestamps) or derives them from
    ///     state (state/delete_time/purge_time). <see cref="ICanonicalName.CanonicalName" /> and
    ///     <see cref="IFreshness.EntityTag" /> are the CLR targets of the AIP wire fields <c>name</c>
    ///     and <c>etag</c>, so they are cleared alongside the internal <see cref="ICanonicalName.Name" />.
    /// </summary>
    public static readonly string[] SystemFields = [
        nameof(ICanonicalName.Name),
        nameof(ICanonicalName.CanonicalName),
        nameof(IConcurrency.Timestamp),
        nameof(IIdentifier.Uid),
        nameof(IOwnable.Owner),
        nameof(IStateful.State),
        nameof(ITimestamp.CreateTime),
        nameof(ITimestamp.UpdateTime),
        nameof(ISoftDelete.DeleteTime),
        nameof(ISoftDelete.PurgeTime),
    ];
}

/// <summary>
///     Clears server-managed fields on an Update request and strips matching paths from the update
///     mask. Mask stripping prevents clients from clearing fields such as <c>owner</c> by setting
///     <c>update_mask=owner</c> after the payload field is ignored.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying update data.</typeparam>
public sealed class AdviceUpdateRequestSanitize<TEntity, TRequest> : IResourceUpdateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceUpdateRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceUpdateRequestSanitize.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        AdviceCreateRequestSanitize.ClearSystemFields(request, AdviceUpdateRequestSanitize.SystemFields);

        if (request is not IUpdateMask { UpdateMask: { } mask } mut) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var remaining = mask.Split(',')
                            .Select(f => f.Trim())
                            .Where(f => f.Length != 0 && !AdviceUpdateRequestSanitize.SystemFields.Contains(ResourceWireNameRules.ResolveClrName(typeof(TRequest), f.Split('.')[0])));

        mut.UpdateMask = string.Join(",", remaining);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
