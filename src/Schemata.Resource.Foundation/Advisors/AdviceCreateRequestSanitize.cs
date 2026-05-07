using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Order constants and system-managed wire fields for
///     <see cref="AdviceCreateRequestSanitize{TEntity, TRequest}" />.
/// </summary>
public static class AdviceCreateRequestSanitize
{
    /// <summary>
    ///     Default order at <see cref="Orders.Base" /> — runs early so cached responses
    ///     short-circuit before authorization and validation.
    /// </summary>
    public const int DefaultOrder = Orders.Base;

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
}

/// <summary>
///     Silently clears server-managed fields on a Create request before validation and authorization. Fields are
///     matched by their snake_case wire name (converted via <see cref="Humanizer.InflectorExtensions.Pascalize" />)
///     against properties on <typeparamref name="TRequest" />; properties that do not exist on the request type are
///     skipped. Required to satisfy AIP-133 immutability rules without surfacing errors to the client.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
public sealed class AdviceCreateRequestSanitize<TEntity, TRequest> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    /// <inheritdoc />
    public int Order => AdviceCreateRequestSanitize.DefaultOrder;

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
            if (!properties.TryGetValue(field, out var property)) {
                continue;
            }

            if (!property.CanWrite) {
                continue;
            }

            var @default = property.PropertyType.IsValueType
                ? Activator.CreateInstance(property.PropertyType)
                : null;
            property.SetValue(request, @default);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
