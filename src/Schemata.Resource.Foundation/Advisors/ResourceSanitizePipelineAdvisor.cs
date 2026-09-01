using System;
using System.Collections.Generic;
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
///     System-managed wire fields and the scrub routine shared by
///     <see cref="ResourceCreateSanitizePipelineAdvisor{TEntity,TRequest,TDetail}" /> and
///     <see cref="ResourceUpdateSanitizePipelineAdvisor{TEntity,TRequest,TDetail}" />.
/// </summary>
public static class ResourceSanitizePipelineAdvisor
{
    /// <summary>
    ///     CLR property names of server-managed fields on Create requests. The server
    ///     assigns them (name/canonical_name/uid/owner/etag/timestamps) or derives them from
    ///     state (state/delete_time/purge_time). <see cref="ICanonicalName.CanonicalName" /> and
    ///     <see cref="IFreshness.EntityTag" /> are the CLR targets of the AIP wire fields <c>name</c>
    ///     and <c>etag</c>, so they are cleared alongside the internal <see cref="ICanonicalName.Name" />.
    /// </summary>
    public static readonly string[] CreateSystemFields = [
        nameof(ICanonicalName.Name),
        nameof(ICanonicalName.CanonicalName),
        nameof(IConcurrency.Timestamp),
        nameof(IFreshness.EntityTag),
        nameof(IIdentifier.Uid),
        nameof(IOwnable.Owner),
        nameof(IStateful.State),
        nameof(ITimestamp.CreateTime),
        nameof(ITimestamp.UpdateTime),
        nameof(ISoftDelete.DeleteTime),
        nameof(ISoftDelete.PurgeTime),
    ];

    /// <summary>
    ///     CLR property names of fields that clients MUST NOT populate on an Update request. The server
    ///     either assigns them (name/canonical_name/uid/owner/timestamps) or derives them from
    ///     state (state/delete_time/purge_time).
    /// </summary>
    public static readonly string[] UpdateSystemFields = [
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

    /// <summary>
    ///     Clears every property on <paramref name="request" /> whose name matches an entry in
    ///     <paramref name="fields" />. Shared by Create and Update sanitize so the field list
    ///     stays single-sourced.
    /// </summary>
    /// <typeparam name="TRequest">The request DTO type.</typeparam>
    /// <param name="request">The request instance to scrub.</param>
    /// <param name="fields">The fields to scrub.</param>
    public static void ClearSystemFields<TRequest>(TRequest request, IEnumerable<string> fields) where TRequest : class {
        var properties = AppDomainTypeCache.GetProperties(typeof(TRequest));

        foreach (var field in fields) {
            if (!properties.TryGetValue(field, out var property) || !property.CanWrite) {
                continue;
            }

            var @default = property.PropertyType.IsValueType
                ? Activator.CreateInstance(property.PropertyType)
                : null;
            property.SetValue(request, @default);
        }
    }
}

/// <summary>
///     Clears server-managed fields on a Create request on the wrap pipeline, before the handler maps
///     the payload. Fields are matched against properties on <typeparamref name="TRequest" />;
///     unknown fields are skipped. This satisfies AIP-133 immutability rules while accepting extra
///     client-supplied field values.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
/// <typeparam name="TDetail">The resource detail response type.</typeparam>
public sealed class ResourceCreateSanitizePipelineAdvisor<TEntity, TRequest, TDetail>
    : IRequestPipelineAdvisor<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IRequestPipelineAdvisor<CreateResourceRequest<TEntity,TRequest,TDetail>,CreateResultBase<TDetail>> Members

    public int Order => SecurityOrders.Sanitize;

    public Task<CreateResultBase<TDetail>> AdviseAsync(
        AdviceContext                                         ctx,
        CreateResourceRequest<TEntity, TRequest, TDetail>     request,
        RequestHandlerContinuation<CreateResultBase<TDetail>> next,
        CancellationToken                                     ct
    ) {
        ResourceSanitizePipelineAdvisor.ClearSystemFields(request.Request, ResourceSanitizePipelineAdvisor.CreateSystemFields);

        return next(ct);
    }

    #endregion
}

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
