using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

/// <summary>Loads persisted waiting processes into the runtime cache.</summary>
internal static class ProcessHydrator
{
    /// <summary>Hydrates runtime cache entries for persisted processes that are waiting at a BPMN element.</summary>
    public static async Task HydrateWaitingAsync(
        ProcessRuntime                runtime,
        IRepository<SchemataProcess> processes,
        CancellationToken             ct
    ) {
        await foreach (var process in processes.ListAsync<SchemataProcess>(
                           q => q.Where(p => p.WaitingAtId != null), ct)) {
            runtime.Hydrate(process);
        }
    }
}
