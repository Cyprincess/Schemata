using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     Starts the scheduler and hands each <see cref="SchemataSchedulingOptions.Jobs" /> registration
///     to <see cref="IScheduler.ScheduleAsync(Schemata.Scheduling.Skeleton.Entities.SchemataJob, System.Threading.CancellationToken)" /> on host startup;
///     stops the scheduler on host shutdown.
/// </summary>
public sealed class SchedulingInitializer : BackgroundService
{
    private readonly IOptions<SchemataSchedulingOptions> _options;
    private readonly IScheduler                          _scheduler;

    public SchedulingInitializer(
        IScheduler                          scheduler,
        IOptions<SchemataSchedulingOptions> options
    ) {
        _scheduler = scheduler;
        _options   = options;
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        await _scheduler.StartAsync(st);

        foreach (var registration in _options.Value.Jobs) {
            var job = new SchemataJob {
                Name    = registration.JobType.FullName!,
                JobType = registration.JobType.AssemblyQualifiedName!,
                State   = JobState.Active,
            };
            ScheduleDefinitionMapper.ApplyToJob(registration.Schedule, job);

            await _scheduler.ScheduleAsync(job, st);
        }
    }

    public override async Task StopAsync(CancellationToken ct) {
        await _scheduler.StopAsync(ct);
        await base.StopAsync(ct);
    }
}
