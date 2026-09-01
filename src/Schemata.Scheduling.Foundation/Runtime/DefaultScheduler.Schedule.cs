using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Runtime;

public sealed partial class DefaultScheduler
{
    public async Task ScheduleAsync(SchemataJob job, CancellationToken ct) {
        using var scope      = _services.CreateScope();
        var       dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        await dispatcher.SendAsync<ScheduleJobRequest, Unit>(new(job, null), ct);
    }

    public async Task ScheduleAsync(
        SchemataJob                           job,
        IReadOnlyDictionary<string, string?>? variables,
        CancellationToken                    ct
    ) {
        using var scope      = _services.CreateScope();
        var       dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        await dispatcher.SendAsync<ScheduleJobRequest, Unit>(new(job, variables, ReplaceVariables: true), ct);
    }

    public async Task UnscheduleAsync(string jobCanonical, CancellationToken ct) {
        using var scope      = _services.CreateScope();
        var       dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        await dispatcher.SendAsync<UnscheduleJobRequest, Unit>(new(jobCanonical), ct);
    }
}
