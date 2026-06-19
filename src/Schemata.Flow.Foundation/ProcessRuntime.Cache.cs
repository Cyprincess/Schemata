using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

/// <summary>Coordinates process runtime operations against registered Flow engines.</summary>
public sealed partial class ProcessRuntime
{
    /// <summary>Adds an already-materialised process to the cache while leaving lifecycle observers silent.</summary>
    public void Hydrate(SchemataProcess process) {
        if (!string.IsNullOrEmpty(process.CanonicalName)) {
            _instances[process.CanonicalName] = process;
        }
    }

    /// <summary>Removes a process from the cache while leaving lifecycle observers silent.</summary>
    public bool Evict(string canonicalName) {
        return _instances.TryRemove(canonicalName, out var _);
    }

    /// <summary>Evicts a cached process so the next runtime operation reloads it from the repository.</summary>
    public void Invalidate(string canonicalName) {
        _instances.TryRemove(canonicalName, out var _);
    }

    /// <summary>Reloads a persisted process row and refreshes the cache entry.</summary>
    public async Task<SchemataProcess?> ReloadAsync(string canonicalName, CancellationToken ct) {
        using var scope = _services.CreateScope();

        var process = await _persistence.FindAsync(scope.ServiceProvider, canonicalName, ct);
        if (process is null) {
            _instances.TryRemove(canonicalName, out var _);
            return null;
        }

        Hydrate(process);
        return process;
    }
}
