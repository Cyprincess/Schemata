using System;
using System.Collections.Generic;
using System.Reflection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;

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

    /// <summary>
    ///     Retains the loaded <paramref name="target" /> values of every listed system field across
    ///     <paramref name="map" />. A scrubbed update request carries value-type defaults
    ///     (<see cref="Guid.Empty" /> uid/timestamps) that merge mapping treats as populated, so the
    ///     update mapping boundary restores the loaded identity and concurrency values afterward.
    /// </summary>
    /// <typeparam name="TTarget">The entity type being updated.</typeparam>
    /// <param name="target">The loaded entity the update maps onto.</param>
    /// <param name="fields">The system fields to retain.</param>
    /// <param name="map">The mapping action to run.</param>
    public static void RetainSystemFields<TTarget>(TTarget target, IEnumerable<string> fields, Action map) where TTarget : class {
        var properties = AppDomainTypeCache.GetProperties(typeof(TTarget));

        List<(PropertyInfo Property, object? Value)>? saved = null;
        foreach (var field in fields) {
            if (!properties.TryGetValue(field, out var property) || !property.CanRead || !property.CanWrite) {
                continue;
            }

            (saved ??= []).Add((property, property.GetValue(target)));
        }

        map();

        if (saved is null) {
            return;
        }

        foreach (var (property, value) in saved) {
            property.SetValue(target, value);
        }
    }
}