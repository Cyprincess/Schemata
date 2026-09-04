using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Event.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class SchedulingFoundationShould
{
    [Fact]
    public async Task Foundation_WithoutEventBridge_SchedulesWithoutPublishingLifecycleEvents() {
        var registry = new Mock<IScheduledJobRegistry>();
        registry.Setup(current => current.ResolveKey(typeof(SampleJob))).Returns("sample-key");

        var executions = new Mock<IRepository<SchemataJobExecution>>();
        executions.Setup(current => current.AddAsync(
                       It.IsAny<SchemataJobExecution>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        executions.Setup(current => current.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var events = new Mock<IEventBus>();
        await using var services = new ServiceCollection()
            .AddSingleton(registry.Object)
            .AddSingleton(executions.Object)
            .AddSingleton(events.Object)
            .AddSchemataScheduling()
            .BuildServiceProvider();
        var scheduler = services.GetRequiredService<IScheduler>();
        await scheduler.StartAsync(CancellationToken.None);

        var execution = await scheduler.TriggerAsync<SampleJob>(new() {
            Job       = "jobs/sample",
            StartTime = DateTime.UtcNow.AddHours(1),
        }, CancellationToken.None);

        Assert.Equal(ExecutionState.Pending, execution.State);
        events.VerifyNoOtherCalls();
        await scheduler.StopAsync(CancellationToken.None);
    }

    private sealed class SampleJob : IScheduledJob
    {
        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }
    }
}
