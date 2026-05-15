using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

internal sealed class SchedulingInitializer : BackgroundService
{
    private readonly IRepository<SchemataJob>            _jobs;
    private readonly IOptions<SchemataSchedulingOptions> _options;
    private readonly IScheduler                          _scheduler;

    public SchedulingInitializer(
        IRepository<SchemataJob>            jobs,
        IScheduler                          scheduler,
        IOptions<SchemataSchedulingOptions> options
    ) {
        _jobs      = jobs;
        _scheduler = scheduler;
        _options   = options;
    }

    protected override async Task ExecuteAsync(CancellationToken st) {
        foreach (var registration in _options.Value.Jobs) {
            var job = new SchemataJob {
                JobType = registration.JobType.AssemblyQualifiedName!,
                State   = JobState.Active,
            };
            ScheduleDefinitionMapper.ApplyToJob(registration.Schedule, job);

            var existing = await _jobs.FirstOrDefaultAsync(q => q.Where(j => j.JobType == job.JobType), st);
            if (existing == null) {
                await _jobs.AddAsync(job, st);
                continue;
            }

            existing.ScheduleType   = job.ScheduleType;
            existing.NextRunTime    = job.NextRunTime;
            existing.IntervalTicks  = job.IntervalTicks;
            existing.CronExpression = job.CronExpression;
            existing.State          = JobState.Active;

            await _jobs.UpdateAsync(existing, st);
        }

        await _jobs.CommitAsync(st);
        await _scheduler.StartAsync(st);
    }
}
