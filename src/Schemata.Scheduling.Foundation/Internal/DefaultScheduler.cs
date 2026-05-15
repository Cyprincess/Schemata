using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation.Internal;

internal sealed class DefaultScheduler : IScheduler
{
    private readonly SemaphoreSlim                                         _lock = new(1, 1);
    private readonly IServiceProvider                                      _services;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    public DefaultScheduler(IServiceProvider services) {
        _services = services;
    }

    #region IScheduler Members

    public async Task StartAsync(CancellationToken ct) {
        using var scope = _services.CreateScope();
        var       jobs  = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();

        var list = await jobs.ListAsync<SchemataJob>(q => q.Where(j => j.State == JobState.Active), ct).ToListAsync(ct);

        foreach (var job in list) {
            await ScheduleJobAsync(job, ct);
        }
    }

    public Task StopAsync(CancellationToken ct) {
        foreach (var cts in _timers.Values) {
            cts.Cancel();
            cts.Dispose();
        }

        _timers.Clear();
        return Task.CompletedTask;
    }

    public async Task ScheduleJobAsync(SchemataJob job, CancellationToken ct) {
        await _lock.WaitAsync(ct);

        try {
            if (string.IsNullOrWhiteSpace(job.Name)) {
                return;
            }
            
            if (_timers.TryGetValue(job.Name, out var existing)) {
                await existing.CancelAsync();
                existing.Dispose();
            }

            var cts = new CancellationTokenSource();
            _timers[job.Name] = cts;

            if (job.NextRunTime.HasValue) {
                var delay = job.NextRunTime.Value - DateTime.UtcNow;
                if (delay > TimeSpan.Zero) {
                    _ = Task.Run(async () => {
                        try {
                            await Task.Delay(delay, cts.Token);
                            if (!cts.Token.IsCancellationRequested) {
                                await ExecuteJobAsync(job.Name, cts.Token);
                            }
                        } catch (OperationCanceledException) {
                            // Expected when unscheduled
                        }
                    }, cts.Token);
                }
            }
        } finally {
            _lock.Release();
        }
    }

    public async Task UnscheduleJobAsync(string jobName, CancellationToken ct) {
        await _lock.WaitAsync(ct);
        try {
            if (_timers.TryRemove(jobName, out var cts)) {
                await cts.CancelAsync();
                cts.Dispose();
            }

            using var    scope = _services.CreateScope();
            var          jobs  = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
            SchemataJob? job   = null;
            await foreach (var j in jobs.ListAsync<SchemataJob>(q => q.Where(j => j.Name == jobName), ct)) {
                job = j;
                break;
            }

            if (job != null) {
                job.State = JobState.Paused;
                await jobs.UpdateAsync(job, ct);
                await jobs.CommitAsync(ct);
            }
        } finally {
            _lock.Release();
        }
    }

    #endregion

    private async Task ExecuteJobAsync(string jobName, CancellationToken ct) {
        using var scope    = _services.CreateScope();
        var       jobRepo  = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJob>>();
        var       execRepo = scope.ServiceProvider.GetRequiredService<IRepository<SchemataJobExecution>>();

        SchemataJob? job = null;
        await foreach (var j in jobRepo.ListAsync<SchemataJob>(q => q.Where(j => j.Name == jobName), ct)) {
            job = j;
            break;
        }

        if (job == null || job.State != JobState.Active) {
            return;
        }

        var execution = new SchemataJobExecution {
            JobName = jobName, State = ExecutionState.Running, StartTime = DateTime.UtcNow,
        };
        await execRepo.AddAsync(execution, ct);
        await execRepo.CommitAsync(ct);

        try {
            var jobType = Type.GetType(job.JobType);
            if (jobType == null) {
                throw new InvalidOperationException($"Job type '{job.JobType}' not found.");
            }

            var scheduledJob = (IScheduledJob)scope.ServiceProvider.GetRequiredService(jobType);

            var variables = string.IsNullOrEmpty(job.Variables)
                ? new()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(job.Variables)!;

            var context = new JobContext { JobName = jobName, Variables = variables };

            await scheduledJob.ExecuteAsync(context, ct);

            execution.State   = ExecutionState.Succeeded;
            execution.EndTime = DateTime.UtcNow;
            await execRepo.UpdateAsync(execution, ct);

            job.LastRunTime = DateTime.UtcNow;
            job.LastError   = null;

            if (job.ScheduleType == ScheduleType.OneTime) {
                job.State       = JobState.Completed;
                job.NextRunTime = null;
            } else if (job.ScheduleType == ScheduleType.Periodic) {
                job.NextRunTime = DateTime.UtcNow + TimeSpan.FromTicks(job.IntervalTicks!.Value);
            } else if (job.ScheduleType == ScheduleType.Cron) {
                var expr = CronExpression.Parse(job.CronExpression!);
                job.NextRunTime = expr.GetNextOccurrence(DateTime.UtcNow, TimeZoneInfo.Utc);
            }

            await jobRepo.UpdateAsync(job, ct);
            await jobRepo.CommitAsync(ct);
            await execRepo.CommitAsync(ct);

            // Reschedule for recurring jobs
            if (job.State == JobState.Active && job.NextRunTime.HasValue) {
                await ScheduleJobAsync(job, ct);
            }
        } catch (Exception ex) {
            execution.State   = ExecutionState.Failed;
            execution.EndTime = DateTime.UtcNow;
            execution.Error   = ex.ToString();
            await execRepo.UpdateAsync(execution, ct);

            job.LastRunTime = DateTime.UtcNow;
            job.LastError   = ex.ToString();
            job.State       = JobState.Failed;
            await jobRepo.UpdateAsync(job, ct);

            await jobRepo.CommitAsync(ct);
            await execRepo.CommitAsync(ct);
        }
    }
}
