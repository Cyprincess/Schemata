using System;
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
            Variables      = new() { ["tier"] = "gold" },
            Replay         = true,
            State          = JobState.Active,
            RecentRunTime  = new DateTime(2026, 8, 25, 11, 0, 0, DateTimeKind.Utc),
            RecentError    = "previous crash",
            NextRunTime    = new DateTime(2026, 8, 26, 13, 0, 0, DateTimeKind.Utc),
        };
        var (services, jobs) = Harness(persisted);
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();

        await dispatcher.SendAsync<StageJobExecutionResultRequest, Unit>(
            new("jobs/sample", JobState.Failed, recentRun, "dispatcher reported failure", null, persisted.ScheduleVersion), CancellationToken.None);

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
        Assert.NotNull(persisted.Variables);
        Assert.Equal("gold", persisted.Variables!["tier"]);
        Assert.True(persisted.Replay);

        jobs.Verify(r => r.UpdateAsync(persisted, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Preserve_Replacement_When_An_Older_Occurrence_Completes() {
        var next = new DateTime(2035, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var persisted = new SchemataJob { CanonicalName = "jobs/replaced", ScheduleVersion = Guid.NewGuid(), State = JobState.Active, NextRunTime = next };
        var (services, jobs) = Harness(persisted);
        using (services) {
            await services.GetRequiredService<IRequestDispatcher>().SendAsync<StageJobExecutionResultRequest, Unit>(
                new(persisted.CanonicalName, JobState.Completed, next, null, null, Guid.NewGuid()));
        }
        Assert.Equal(JobState.Active, persisted.State);
        Assert.Equal(next, persisted.NextRunTime);
        jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stage_Missing_Job_Performs_No_Write() {
        var (services, jobs) = Harness(null);
        var dispatcher = services.GetRequiredService<IRequestDispatcher>();

        await dispatcher.SendAsync<StageJobExecutionResultRequest, Unit>(
            new("jobs/absent", JobState.Failed,
                new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc), "dispatcher reported failure", null, Guid.Empty),
            CancellationToken.None);

        jobs.Verify(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
        jobs.Verify(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()), Times.Never);
        jobs.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (ServiceProvider Services, Mock<IRepository<SchemataJob>> Jobs) Harness(SchemataJob? persisted) {
        var jobs = new Mock<IRepository<SchemataJob>>();
        jobs.Setup(r => r.Begin()).Returns(CommittingUnitOfWork());
        jobs.Setup(r => r.FirstOrDefaultAsync(
                 It.IsAny<Func<IQueryable<SchemataJob>, IQueryable<SchemataJob>>>(),
                 It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<SchemataJob?>(persisted));
        jobs.Setup(r => r.AddAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);
        jobs.Setup(r => r.UpdateAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>())).ReturnsAsync(MutationResult.Applied);

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(r => r.AddAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MutationResult.Applied);
        executions.Setup(r => r.UpdateAsync(It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(MutationResult.Applied);

        var jobMutation = new Mock<IResourceMutation<SchemataJob>>();
        jobMutation.Setup(m => m.CreateAsync(It.IsAny<SchemataJob>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
                   .Returns(async (SchemataJob entity, IUnitOfWork? _, CancellationToken c) => {
                        await jobs.Object.AddAsync(entity, c);
                        return MutationResult.Applied;
                    });
        jobMutation.Setup(m => m.UpdateAsync(
                              It.IsAny<SchemataJob>(), It.IsAny<IUnitOfWork?>(),
                              It.IsAny<Schemata.Abstractions.Entities.Operations>(), It.IsAny<CancellationToken>()))
                   .Returns(async (SchemataJob entity, IUnitOfWork? _, Schemata.Abstractions.Entities.Operations _, CancellationToken c) => {
                        await jobs.Object.UpdateAsync(entity, c);
                        return MutationResult.Applied;
                    });

        var executionMutation = new Mock<IResourceMutation<SchemataJobExecution>>();
        executionMutation.Setup(m => m.CreateAsync(
                                    It.IsAny<SchemataJobExecution>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
                         .Returns(async (SchemataJobExecution entity, IUnitOfWork? _, CancellationToken c) => {
                              await executions.Object.AddAsync(entity, c);
                              return MutationResult.Applied;
                          });
        executionMutation.Setup(m => m.UpdateAsync(
                                    It.IsAny<SchemataJobExecution>(), It.IsAny<IUnitOfWork?>(),
                                    It.IsAny<Schemata.Abstractions.Entities.Operations>(), It.IsAny<CancellationToken>()))
                         .Returns(async (SchemataJobExecution entity, IUnitOfWork? _, Schemata.Abstractions.Entities.Operations _, CancellationToken c) => {
                              await executions.Object.UpdateAsync(entity, c);
                              return MutationResult.Applied;
                          });

        var services = new ServiceCollection()
                      .AddSingleton(jobs.Object)
                      .AddSingleton(executions.Object)
                      .AddSingleton(jobMutation.Object)
                      .AddSingleton(executionMutation.Object)
                      .AddSingleton<IOptions<SchemataSchedulingOptions>>(Options.Create(new SchemataSchedulingOptions()))
                      .AddSchemataScheduling()
                      .BuildServiceProvider();

        return (services, jobs);
    }

    private static IUnitOfWork CommittingUnitOfWork() {
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(work => work.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unit.Setup(work => work.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return unit.Object;
    }
}
