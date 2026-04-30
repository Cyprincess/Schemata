using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>Order constants and system-managed wire fields for <see cref="AdviceUpdateRequestSanitize{TEntity, TRequest}" />.</summary>
public static class AdviceUpdateRequestSanitize
{
    /// <summary>
    ///     Default order at <see cref="Orders.Base" />.
    /// </summary>
    public const int DefaultOrder = Orders.Base;
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

    /// <inheritdoc />
    public int Order => AdviceUpdateRequestSanitize.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        var properties = AppDomainTypeCache.GetProperties(typeof(TRequest));

        foreach (var field in AdviceCreateRequestSanitize.SystemFields) {
            if (!properties.TryGetValue(field, out var property) || !property.CanWrite) {
                continue;
            }

            var @default = property.PropertyType.IsValueType
                ? Activator.CreateInstance(property.PropertyType)
                : null;
            property.SetValue(request, @default);
        }

        if (request is IUpdateMask { UpdateMask: { } mask } mut) {
            var remaining = mask.Split(',')
                                .Select(f => f.Trim())
                                .Where(f => f.Length != 0 && !AdviceCreateRequestSanitize.SystemFields.Contains(f.Pascalize()));

            mut.UpdateMask = string.Join(",", remaining);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
