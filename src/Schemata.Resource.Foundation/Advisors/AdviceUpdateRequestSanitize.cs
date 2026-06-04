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
}

/// <summary>
///     Silently clears server-managed fields on an Update request and strips matching paths from the update
///     mask. Without the mask strip, a client could clear <c>owner</c> by setting <c>update_mask=owner</c> even
///     though the payload field was ignored, because partial-update merges the mask — not the payload — into the
///     entity.
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
        AdviceCreateRequestSanitize.ClearSystemFields(request);

        if (request is IUpdateMask { UpdateMask: { } mask } mut) {
            var remaining = mask.Split(',')
                                .Select(f => f.Trim())
                                .Where(f => f.Length != 0 && !AdviceCreateRequestSanitize.SystemFields.Contains(SchemataNaming.ToClrMemberName(f)));

            mut.UpdateMask = string.Join(",", remaining);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
