using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Foundation.Commands;
using Schemata.Push.Scheduling.Handlers;
using Schemata.Push.Scheduling.Runtime;
using Schemata.Push.Skeleton;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Tests;

public class ScheduledPushServiceShould
{
    [Fact]
    public async Task ScheduleSendAsync_CapturesTriggerArgs_And_ReturnsMatchingOperation()
    {
        JobContext? staged = null;
        CancellationToken capturedToken = default;
        var uid         = Guid.NewGuid();
        var scheduledAt = DateTimeOffset.Parse("2025-01-15T09:30:00+02:00");
        var context     = new PushContext("payload", new TopicTarget("alerts"));
        using var cts   = new CancellationTokenSource();
        var execution = new SchemataJobExecution {
            Uid          = uid,
            CanonicalName = $"operations/{uid:n}",
            Method       = "send",
            State        = ExecutionState.Pending,
        };

        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(s => s.TriggerAsync<PushDispatchJob>(
                It.IsAny<JobContext>(),
                It.IsAny<CancellationToken>()))
            .Callback((JobContext ctx, CancellationToken token) => {
                staged       = ctx;
                capturedToken = token;
            })
            .ReturnsAsync(execution);

        using var services = BuildServices(scheduler.Object);
        var service = new ScheduledPushService(services.GetRequiredService<IRequestDispatcher>());

        var operation = await service.ScheduleSendAsync(context, scheduledAt, cts.Token);

        Assert.NotNull(staged);

        Assert.NotNull(staged.ArgsJson);
        var roundTrip = JsonSerializer.Deserialize<PushContext>(staged.ArgsJson, SchemataJson.Default);
        Assert.NotNull(roundTrip);
        Assert.Equal("payload", Assert.IsType<JsonElement>(roundTrip.Message).GetString());
        Assert.Equal("alerts", Assert.IsType<TopicTarget>(roundTrip.Target).Topic);
        Assert.Equal(cts.Token, capturedToken);

        Assert.Equal("send", staged.Method);

        Assert.NotNull(staged.ExecutionUid);
        Assert.NotEqual(Guid.Empty, staged.ExecutionUid.Value);

        Assert.NotNull(staged.StartTime);
        Assert.Equal(DateTimeKind.Utc, staged.StartTime.Value.Kind);
        Assert.Equal(scheduledAt.UtcDateTime, staged.StartTime.Value);


        Assert.Equal(execution.CanonicalName, operation.CanonicalName);
        Assert.False(operation.Done);
    }

    [Fact]
    public async Task ScheduleSendAsync_Without_Time_Leaves_StartTime_Null() {
        JobContext? staged = null;
        var scheduler = new Mock<IScheduler>();
        scheduler.Setup(value => value.TriggerAsync<PushDispatchJob>(
                            It.IsAny<JobContext>(), It.IsAny<CancellationToken>()))
                 .Callback((JobContext context, CancellationToken _) => staged = context)
                 .ReturnsAsync(new SchemataJobExecution {
                     Uid           = Guid.NewGuid(),
                     CanonicalName = $"operations/{Guid.NewGuid():n}",
                     State         = ExecutionState.Pending,
                 });
        using var services = BuildServices(scheduler.Object);
        var service = new ScheduledPushService(services.GetRequiredService<IRequestDispatcher>());

        await service.ScheduleSendAsync(
            new PushContext("payload", new TopicTarget("alerts")),
            null,
            CancellationToken.None);

        Assert.NotNull(staged);
        Assert.Null(staged.StartTime);
    }
    private static ServiceProvider BuildServices(IScheduler scheduler) {
        var services = new ServiceCollection();
        services.AddSingleton(scheduler);
        services.AddSchemataPush();
        services.AddSingleton<IRequestHandler<SchedulePushRequest, Abstractions.Resource.Operation>,
            SchedulePushHandler>();
        return services.BuildServiceProvider();
    }

}
