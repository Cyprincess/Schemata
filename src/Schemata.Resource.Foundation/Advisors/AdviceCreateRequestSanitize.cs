using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Order constants and system-managed wire fields for
///     <see cref="AdviceCreateRequestSanitize{TEntity, TRequest}" />.
/// </summary>
public static class AdviceCreateRequestSanitize
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceCreateRequestAuthorize{TEntity,TRequest}" />
    ///     so authorization decisions are made against the unaltered client payload, then this
    ///     advisor clears server-managed fields before validation reads them.
    /// </summary>
    public const int DefaultOrder = AdviceCreateRequestAuthorize.DefaultOrder + 10_000_000;

    /// <summary>
    ///     Wire-level field names that clients MUST NOT populate on a Create request. The server
    ///     either assigns them (name/id/uid/owner/timestamps) or derives them from state (state/
    ///     delete_time/purge_time).
    /// </summary>
    public static readonly string[] SystemFields = [
        nameof(ICanonicalName.Name),
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
    ///     <see cref="SystemFields" />. Shared by Create and Update sanitize so the field list
    ///     stays single-sourced.
    /// </summary>
    /// <typeparam name="TRequest">The request DTO type.</typeparam>
    /// <param name="request">The request instance to scrub.</param>
    public static void ClearSystemFields<TRequest>(TRequest request) where TRequest : class {
        var properties = AppDomainTypeCache.GetProperties(typeof(TRequest));

        foreach (var field in SystemFields) {
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
///     Silently clears server-managed fields on a Create request before validation and authorization. Fields are
///     matched by their snake_case wire name against properties on <typeparamref name="TRequest" />; properties that do
///     not exist on the request type are
///     skipped. Required to satisfy AIP-133 immutability rules without surfacing errors to the client.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
public sealed class AdviceCreateRequestSanitize<TEntity, TRequest> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceCreateRequestSanitize.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        AdviceCreateRequestSanitize.ClearSystemFields(request);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
