using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public sealed class StageJobExecutionResultHandlerShould
{
    [Fact]
    public async Task Stage_Existing_Job_Overwrites_Result_Fields_And_Preserves_Schedule_Configuration() {
        var recentRun = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
        var persisted = new SchemataJob {
            CanonicalName  = "jobs/sample",
            Name           = "sample",
            JobKey         = "sample-key",
            ScheduleType   = ScheduleType.Cron,
            CronExpression = "0 * * * *",
            ArgsJson       = """{"count":3}""",
            Variables      = new Dictionary<string, string?> { ["tier"] = "gold" },
            Replay         = true,
            State          = JobState.Active,
            RecentRunTime  = new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc),
            RecentError    = "previous crash",
            NextRunTime    = new DateTime(2026, 8, 26, 13, 0, 0, DateTimeKind.Utc),
        };
        var (services, jobs) = Harness(persisted);
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();

        await dispatcher.SendAsync<StageJobExecutionResultRequest, Unit>(
            new("jobs/sample", JobState.Failed, recentRun, "dispatcher reported failure", null), CancellationToken.None);

        Assert.Equal(JobState.Failed, persisted.State);
        Assert.Equal(recentRun, persisted.RecentRunTime);
        Assert.Equal("dispatcher reported failure", persisted.RecentError);
        Assert.Null(persisted.NextRunTime);

        Assert.Equal("jobs/sample", persisted.CanonicalName);
        Assert.Equal("sample", persisted.Name);
        Assert.Equal("sample-key", persisted.JobKey);
        Assert.Equal(ScheduleType.Cron, persisted.ScheduleType);
        Assert.Equal("0 * * * *", persisted.CronExpression);
        Assert.Equal("""{"count":3}""", persisted.ArgsJson);
        Assert.Equal("gold", persisted.Variables!["tier"]);
        Assert.True(persisted.Replay);

        jobs.Verify(r => r.UpdateAsync(persisted, It.IsAny<CancellationToken>()), Times.Once);
        jobs.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Stage_Missing_Job_Performs_No_Write() {
        var (services, jobs) = Harness(null);
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();

        await dispatcher.SendAsync<StageJobExecutionResultRequest, Unit>(
            new("jobs/absent", JobState.Failed,
                new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc), "dispatcher reported failure", null),
            CancellationToken.None);

        jobs.Verify(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
        jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
        jobs.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (ServiceProvider Services, Mock<IRepository<SchemataJob>> Jobs) Harness(SchemataJob? persisted) {
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.FirstOrDefaultAsync(
                 It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                 It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(persisted));
        jobs.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        jobs.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var services = new ServiceCollection()
                      .AddSingleton(jobs.Object)
                      .AddSingleton(executions.Object)
                      .AddSingleton<IOptions<SchemataSchedulingOptions>>(Options.Create(new SchemataSchedulingOptions()))
                      .AddSchemataScheduling()
                      .BuildServiceProvider();

        return (services, jobs);
    }
}
