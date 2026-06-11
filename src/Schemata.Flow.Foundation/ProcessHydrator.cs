using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

internal static class ProcessHydrator
{
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
