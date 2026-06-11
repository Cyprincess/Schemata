using System;
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
    private readonly IProcessRuntime  _runtime;
    private readonly IServiceProvider _services;

    public ProcessInitializer(IProcessRuntime runtime, IServiceProvider services) {
        _runtime  = runtime;
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        if (_runtime is not ProcessRuntime concrete) {
            return;
        }

        var processes = _services.GetRequiredService<IRepository<SchemataProcess>>();

        await ProcessHydrator.HydrateWaitingAsync(concrete, processes, st);
    }
}
