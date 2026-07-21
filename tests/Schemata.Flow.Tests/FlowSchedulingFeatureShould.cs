using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Core;
using Schemata.Flow.Scheduling.Features;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Flow.Tests;

public sealed class FlowSchedulingFeatureShould
{
    [Fact]
    public async Task Create_Cycle_Timer_Preserves_Cron_And_Correlated_Payload() {
        var scheduler = new Mock<IScheduler>(MockBehavior.Strict);
        SchemataJob?                            scheduled = null;
        IReadOnlyDictionary<string, string?>? variables = null;
        scheduler.Setup(current => current.ScheduleAsync(
                             It.IsAny<SchemataJob>(),
                             It.IsAny<IReadOnlyDictionary<string, string?>?>(),
                             It.IsAny<CancellationToken>()))
                 .Callback<SchemataJob, IReadOnlyDictionary<string, string?>?, CancellationToken>((job, payload, _) => {
                     scheduled = job;
                     variables = payload;
                 })
                 .Returns(Task.CompletedTask);

        await using var services = CreateServices(scheduler);
        var advisor = Assert.Single(services.GetServices<IFlowTransitionAdvisor>());

        var result = await advisor.AdviseAsync(new AdviceContext(services), Context(), CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.NotNull(scheduled);
        Assert.Equal("flow-p1-timer-t1", scheduled!.Name);
        Assert.Equal(ScheduleType.Cron, scheduled.ScheduleType);
        Assert.Equal("*/5 * * * *", scheduled.CronExpression);
        Assert.NotNull(variables);
        Assert.Equal("processes/p1", variables!["processName"]);
        Assert.Equal("processes/p1/tokens/t1", variables["tokenName"]);
        Assert.NotNull(variables["timerDef"]);
        scheduler.Verify(current => current.ScheduleAsync(
                             scheduled,
                             variables,
                             CancellationToken.None), Times.Once);
        scheduler.VerifyNoOtherCalls();
    }

    private static ServiceProvider CreateServices(Mock<IScheduler> scheduler) {
        var services = new ServiceCollection();
        services.AddSingleton(scheduler.Object);
        new SchemataFlowSchedulingFeature().ConfigureServices(
            services, new(), new(), new ConfigurationBuilder().Build(), null!);
        return services.BuildServiceProvider();
    }

    private static FlowTransitionContext Context() {
        var definition = new ProcessDefinition();
        definition.Elements.Add(new FlowEvent {
            Name       = "timer",
            Position   = EventPosition.IntermediateCatch,
            Definition = new TimerDefinition { Name = "reminder", TimerType = TimerType.Cycle, TimeExpression = "*/5 * * * *" },
        });

        return new() {
            Definition = definition,
            Snapshot = new() {
                Process     = new() { CanonicalName = "processes/p1" },
                Tokens      = [],
                Transitions = [],
            },
            Token = new() {
                CanonicalName = "processes/p1/tokens/t1",
                ScopeName     = "p1",
                StateName     = "timer",
                WaitingAtName = "timer",
                Status        = "Waiting",
            },
        };
    }
}
