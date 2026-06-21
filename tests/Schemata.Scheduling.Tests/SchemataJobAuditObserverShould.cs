using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Observers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class SchemataJobAuditObserverShould
{
    private static (SchemataJobAuditObserver observer, Mock<IRepository<SchemataJob>> jobs) Build() {
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new(jobs.Object), jobs);
    }

    [Fact]
    public async Task InsertSchemataJob_WhenOnScheduledAndNoExistingRow() {
        var (observer, jobs) = Build();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                                              It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>((SchemataJob?)null));

        var job = new SchemataJob {
            Name         = "test/cron",
            JobKey       = "test.JobKey",
            ScheduleType = ScheduleType.Cron,
            State        = JobState.Active,
        };

        await observer.OnScheduledAsync(job);

        jobs.Verify(r => r.AddAsync(job, It.IsAny<CancellationToken>()), Times.Once);
        jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
        jobs.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateExistingRow_WhenOnScheduledAndExistingRowFound() {
        var (observer, jobs) = Build();
        var existing = new SchemataJob {
            Name         = "test/cron",
            JobKey       = "old.JobKey",
            ScheduleType = ScheduleType.OneTime,
            State        = JobState.Active,
        };
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                                              It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existing));

        var incoming = new SchemataJob {
            Name           = "test/cron",
            JobKey         = "new.JobKey",
            ScheduleType   = ScheduleType.Cron,
            CronExpression = "0 * * * *",
            State          = JobState.Active,
            Replay         = true,
        };

        await observer.OnScheduledAsync(incoming);

        jobs.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        jobs.Verify(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkExistingPaused_WhenOnUnscheduledFindsRow() {
        var (observer, jobs) = Build();
        var existing = new SchemataJob { Name = "test/job", State = JobState.Active };
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                                              It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existing));

        await observer.OnUnscheduledAsync(new() { Name = "test/job", State = JobState.Paused });

        Assert.Equal(JobState.Paused, existing.State);
        jobs.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        jobs.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoNothing_WhenOnUnscheduledAndNoExistingRow() {
        var (observer, jobs) = Build();
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                                              It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>((SchemataJob?)null));

        await observer.OnUnscheduledAsync(new() { Name = "test/missing" });

        jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
        jobs.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateJobRow_WhenOnSucceeded() {
        var (observer, jobs) = Build();

        var existingJob = new SchemataJob {
            Name = "test/job", State = JobState.Active, RecentRunTime = null,
        };
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                                              It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existingJob));

        var ctx = new JobContext {
            Job = "test/job", ExecutionUid = Identifiers.NewUid(), StartTime = DateTime.UtcNow.AddSeconds(-5),
        };
        var incoming = new SchemataJob {
            Name          = "test/job",
            RecentRunTime = DateTime.UtcNow,
            State         = JobState.Completed,
            NextRunTime   = null,
        };

        await observer.OnSucceededAsync(incoming, ctx);

        Assert.Equal(JobState.Completed, existingJob.State);
        Assert.Null(existingJob.NextRunTime);
        jobs.Verify(r => r.UpdateAsync(existingJob, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PersistNextRunTime_WhenRecurringJobSucceeds() {
        var (observer, jobs) = Build();
        var next = DateTime.UtcNow.AddMinutes(15);

        var existingJob = new SchemataJob { Name = "test/job", State = JobState.Active };
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                                              It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existingJob));

        await observer.OnSucceededAsync(new() { Name = "test/job", State        = JobState.Active, NextRunTime = next },
                                        new() { Job  = "test/job", ExecutionUid = Identifiers.NewUid() });

        Assert.Equal(next, existingJob.NextRunTime);
        jobs.Verify(r => r.UpdateAsync(existingJob, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFailedStateAndError_WhenOnFailed() {
        var (observer, jobs) = Build();

        var existingJob = new SchemataJob { Name = "test/job", State = JobState.Active };
        jobs.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                                              It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existingJob));

        var ctx = new JobContext {
            Job = "test/job", ExecutionUid = Identifiers.NewUid(), StartTime = DateTime.UtcNow.AddSeconds(-2),
        };
        var incoming = new SchemataJob {
            Name          = "test/job",
            RecentRunTime = DateTime.UtcNow,
            RecentError   = "Boom",
            State         = JobState.Failed,
        };
        var exception = new InvalidOperationException("Boom");

        await observer.OnFailedAsync(incoming, ctx, exception);

        Assert.Equal(JobState.Failed, existingJob.State);
        Assert.Equal("Boom", existingJob.RecentError);
    }
}
