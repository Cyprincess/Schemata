using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scheduling.Foundation.Observers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class SchemataJobAuditObserverShould
{
    private static (SchemataJobAuditObserver observer,
                    Mock<IRepository<SchemataJob>> jobs,
                    Mock<IRepository<SchemataJobExecution>> executions) Build() {
        var jobs       = new Mock<IRepository<SchemataJob>>();
        var executions = new Mock<IRepository<SchemataJobExecution>>();
        jobs.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new(jobs.Object, executions.Object), jobs, executions);
    }

    [Fact]
    public async Task InsertSchemataJob_WhenOnScheduledAndNoExistingRow() {
        var (observer, jobs, _) = Build();
        jobs.Setup(r => r.FirstOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                       It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>((SchemataJob?)null));

        var job = new SchemataJob {
            Name         = "test/cron",
            JobType      = "test.JobType",
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
        var (observer, jobs, _) = Build();
        var existing = new SchemataJob {
            Name         = "test/cron",
            JobType      = "old.JobType",
            ScheduleType = ScheduleType.OneTime,
            State        = JobState.Active,
        };
        jobs.Setup(r => r.FirstOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                       It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existing));

        var incoming = new SchemataJob {
            Name           = "test/cron",
            JobType        = "new.JobType",
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
        var (observer, jobs, _) = Build();
        var existing = new SchemataJob { Name = "test/job", State = JobState.Active };
        jobs.Setup(r => r.FirstOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                       It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existing));

        await observer.OnUnscheduledAsync(new SchemataJob { Name = "test/job", State = JobState.Paused });

        Assert.Equal(JobState.Paused, existing.State);
        jobs.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        jobs.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DoNothing_WhenOnUnscheduledAndNoExistingRow() {
        var (observer, jobs, _) = Build();
        jobs.Setup(r => r.FirstOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                       It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>((SchemataJob?)null));

        await observer.OnUnscheduledAsync(new SchemataJob { Name = "test/missing" });

        jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
        jobs.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PersistContextExecution_WhenOnTriggered() {
        var (observer, _, executions) = Build();
        var job = new SchemataJob { Name = "test/job" };
        var execution = new SchemataJobExecution {
            Uid           = Guid.NewGuid(),
            Job           = "test/job",
            State         = ExecutionState.Pending,
            StartTime     = DateTime.UtcNow,
            Name          = "abc",
            CanonicalName = "operations/abc",
        };
        var ctx = new JobContext {
            Job          = "test/job",
            ExecutionUid = execution.Uid,
            StartTime    = execution.StartTime,
            Execution    = execution,
        };

        var outcome = await observer.OnTriggeredAsync(job, ctx);

        Assert.Equal(JobTriggerOutcome.Proceed, outcome);
        executions.Verify(r => r.AddAsync(execution, It.IsAny<CancellationToken>()), Times.Once);
        executions.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoOp_WhenOnTriggeredWithoutContextExecution() {
        var (observer, _, executions) = Build();
        var job = new SchemataJob { Name = "test/job" };
        var ctx = new JobContext { Job = "test/job", ExecutionUid = Guid.NewGuid() };

        var outcome = await observer.OnTriggeredAsync(job, ctx);

        Assert.Equal(JobTriggerOutcome.Proceed, outcome);
        executions.Verify(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()),
                          Times.Never);
        executions.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateJobAndExecution_WhenOnSucceeded() {
        var (observer, jobs, executions) = Build();

        var existingJob = new SchemataJob {
            Name          = "test/job",
            State         = JobState.Active,
            RecentRunTime = null,
        };
        jobs.Setup(r => r.FirstOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                       It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existingJob));

        var execUid = Guid.NewGuid();
        var existingExec = new SchemataJobExecution {
            Uid     = execUid,
            Job = "test/job",
            State   = ExecutionState.Running,
        };
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<SchemataJobExecution?>(existingExec));

        var ctx = new JobContext {
            Job      = "test/job",
            ExecutionUid = execUid,
            StartTime    = DateTime.UtcNow.AddSeconds(-5),
        };
        var incoming = new SchemataJob {
            Name          = "test/job",
            RecentRunTime = DateTime.UtcNow,
            State         = JobState.Completed,
            NextRunTime   = null,
        };

        await observer.OnSucceededAsync(incoming, ctx);

        Assert.Equal(JobState.Completed,    existingJob.State);
        Assert.Equal(ExecutionState.Succeeded, existingExec.State);
        Assert.NotNull(existingExec.EndTime);
        Assert.Null(existingExec.RecentError);

        jobs.Verify(r => r.UpdateAsync(existingJob, It.IsAny<CancellationToken>()), Times.Once);
        executions.Verify(r => r.UpdateAsync(existingExec, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFailedStateAndError_WhenOnFailed() {
        var (observer, jobs, executions) = Build();

        var existingJob  = new SchemataJob { Name = "test/job", State = JobState.Active };
        var existingExec = new SchemataJobExecution {
            Uid     = Guid.NewGuid(),
            Job = "test/job",
            State   = ExecutionState.Running,
        };
        jobs.Setup(r => r.FirstOrDefaultAsync(
                       It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>?>(),
                       It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(existingJob));
        executions.Setup(r => r.FirstOrDefaultAsync(
                             It.IsAny<Func<IQueryable<SchemataJobExecution>, IQueryable<SchemataJobExecution>>?>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<SchemataJobExecution?>(existingExec));

        var ctx = new JobContext {
            Job      = "test/job",
            ExecutionUid = existingExec.Uid,
            StartTime    = DateTime.UtcNow.AddSeconds(-2),
        };
        var incoming = new SchemataJob {
            Name          = "test/job",
            RecentRunTime = DateTime.UtcNow,
            RecentError   = "Boom",
            State         = JobState.Failed,
        };
        var exception = new InvalidOperationException("Boom");

        await observer.OnFailedAsync(incoming, ctx, exception);

        Assert.Equal(JobState.Failed,      existingJob.State);
        Assert.Equal("Boom",               existingJob.RecentError);
        Assert.Equal(ExecutionState.Failed, existingExec.State);
        Assert.NotNull(existingExec.EndTime);
        Assert.Contains("Boom", existingExec.RecentError);
    }
}
