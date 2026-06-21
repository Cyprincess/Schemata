using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Resolves the stable scheduler key for the closed-generic <see cref="PurgeJob{TEntity}" />.
///     The key is <c>purge:{collection}</c>; the collection segment identifies the soft-deletable
///     resource whose purge runs, so a reloaded purge operation rebuilds <c>PurgeJob&lt;TEntity&gt;</c>
///     after a restart without a per-entity registration.
/// </summary>
public sealed class PurgeJobKeyResolver : IScheduledJobKeyResolver
{
    private static readonly string Prefix = $"{Verbs.Purge}:";

    private readonly IResourceTypeResolver _resolver;

    /// <summary>Creates the purge job key resolver.</summary>
    /// <param name="resolver">Reverse-resolves the collection segment to its entity type.</param>
    public PurgeJobKeyResolver(IResourceTypeResolver resolver) { _resolver = resolver; }

    #region IScheduledJobKeyResolver Members

    public Type? ResolveType(string key) {
        if (!key.StartsWith(Prefix, StringComparison.Ordinal)) {
            return null;
        }

        var collection = key.Substring(Prefix.Length);
        var entity     = _resolver.ResolveCollection(collection);
        if (entity is null || !typeof(ISoftDelete).IsAssignableFrom(entity)) {
            return null;
        }

        return typeof(PurgeJob<>).MakeGenericType(entity);
    }

    public string? ResolveKey(Type jobType) {
        if (!jobType.IsGenericType || jobType.GetGenericTypeDefinition() != typeof(PurgeJob<>)) {
            return null;
        }

        var entity = jobType.GetGenericArguments()[0];
        return $"{Prefix}{ResourceNameDescriptor.ForType(entity).Collection}";
    }

    #endregion
}
