using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Caching.Skeleton;

namespace Schemata.Entity.Cache;

internal static class CacheGeneration<TEntity>
    where TEntity : class
{
    internal static readonly string Key = $"generation\x1e{typeof(TEntity).AssemblyQualifiedName}"
        .ToCacheKey(SchemataConstants.Keys.Entity);

    private static readonly ConditionalWeakTable<object, string> Snapshots = new();

    internal static async Task<string?> CaptureAsync(
        ICacheProvider cache,
        object context,
        string queryKey,
        CancellationToken ct
    ) {
        if (Snapshots.TryGetValue(context, out var captured)) {
            return captured;
        }

        var generation = await cache.GetAsync(Key, ct);
        if (generation is null) {
            // Each candidate is published once, so even a process-local TryAdd racing a commit
            // can only introduce an unused generation, never resurrect one with stale results.
            await cache.TryAddAsync(Key, Guid.NewGuid().ToByteArray(), new(), ct);
            generation = await cache.GetAsync(Key, ct);
            if (generation is null) {
                return null;
            }
        }

        var key = $"{Key}:{Convert.ToHexString(generation)}:{queryKey}";
        return Snapshots.GetValue(context, _ => key);
    }

    internal static bool TryGetKey(object context, out string? key) {
        return Snapshots.TryGetValue(context, out key);
    }
}
