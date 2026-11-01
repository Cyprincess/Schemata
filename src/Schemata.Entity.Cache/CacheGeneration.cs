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
        string? sourceKey,
        CancellationToken ct
    ) {
        // A repository claiming a source it cannot identify must not rotate or read the shared
        // global generation; its query cache stays disabled instead.
        if (sourceKey is not null && string.IsNullOrWhiteSpace(sourceKey)) {
            return null;
        }

        if (Snapshots.TryGetValue(context, out var captured)) {
            return captured;
        }

        var generationKey = GenerationKey(sourceKey);
        var generation = await cache.GetAsync(generationKey, ct);
        if (generation is null) {
            // Read back the winning generation rather than assuming this initializer won.
            await cache.TryAddAsync(generationKey, Guid.NewGuid().ToByteArray(), new(), ct);
            generation = await cache.GetAsync(generationKey, ct);
            if (generation is null) {
                return null;
            }
        }

        var key = $"{generationKey}:{Convert.ToHexString(generation)}:{queryKey}";
        return Snapshots.GetValue(context, _ => key);
    }

    // Repositories without source identity share the global generation; an identified source is
    // scoped so one source's commit eviction never invalidates another source's cached queries.
    internal static string GenerationKey(string? sourceKey) {
        return string.IsNullOrEmpty(sourceKey)
            ? Key
            : $"generation\x1e{typeof(TEntity).AssemblyQualifiedName}\x1e{sourceKey}".ToCacheKey(SchemataConstants.Keys.Entity);
    }

    internal static bool TryGetKey(object context, out string? key) {
        return Snapshots.TryGetValue(context, out key);
    }
}
