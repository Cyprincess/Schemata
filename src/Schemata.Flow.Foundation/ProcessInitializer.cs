using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Hydrates <see cref="ProcessRuntime" /> with waiting
///     <see cref="SchemataProcess" /> rows on host startup.
/// </summary>
public sealed class ProcessInitializer : BackgroundService
{
    private readonly IProcessRuntime      _runtime;
    private readonly IServiceScopeFactory _scopeFactory;

    public ProcessInitializer(IProcessRuntime runtime, IServiceScopeFactory scopeFactory) {
        _runtime      = runtime;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        if (_runtime is not ProcessRuntime concrete) {
            return;
        }

        await using var scope     = _scopeFactory.CreateAsyncScope();
        var             processes = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcess>>();

        await foreach (var process in processes.ListAsync<SchemataProcess>(
                                                q => q.Where(p => p.WaitingAtId != null), st)) {
            concrete.Hydrate(process);
        }
    }
}
