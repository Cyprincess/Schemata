using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class DefaultSchedulerShould
{
    private static (DefaultScheduler scheduler, Mock<IJobLifecycleObserver> observer, ServiceProvider sp) Build() {
        var observer = new Mock<IJobLifecycleObserver>();
        observer.Setup(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnUnscheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnTriggeredAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .ReturnsAsync(JobTriggerOutcome.Proceed);
        observer.Setup(o => o.OnSucceededAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(),
                                               It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnFailedAsync(It.IsAny<SchemataJob>(), It.IsAny<JobContext>(), It.IsAny<Exception>(),
                                            It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(observer.Object);
        services.AddSingleton<IScheduledJobRegistry>(Registry(typeof(NoopJob)));
        services.AddTransient<NoopJob>();

        var sp        = services.BuildServiceProvider();
        var options   = Options.Create(new SchemataSchedulingOptions());
        var scheduler = new DefaultScheduler(sp, options);

        return (scheduler, observer, sp);
    }

    [Fact]
    public async Task NotifyOnScheduledAsync_WhenScheduleAsyncRegistersANewEntry() {
        var (scheduler, observer, sp) = Build();
        try {
            await scheduler.StartAsync(CancellationToken.None);

            var job = new SchemataJob {
                Name         = "test/scheduled/once",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };

            await scheduler.ScheduleAsync(job, CancellationToken.None);

            observer.Verify(
                o => o.OnScheduledAsync(It.Is<SchemataJob>(j => j.Name == "test/scheduled/once"),
                                        It.IsAny<CancellationToken>()), Times.Once);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task NotifyOnUnscheduledAsync_WhenUnscheduleAsyncRemovesAnEntry() {
        var (scheduler, observer, sp) = Build();
        try {
            await scheduler.StartAsync(CancellationToken.None);

            var job = new SchemataJob {
                Name         = "test/unscheduled",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };
            await scheduler.ScheduleAsync(job, CancellationToken.None);

            await scheduler.UnscheduleAsync("test/unscheduled", CancellationToken.None);

            observer.Verify(
                o => o.OnUnscheduledAsync(
                    It.Is<SchemataJob>(j => j.Name == "test/unscheduled" && j.State == JobState.Paused),
                    It.IsAny<CancellationToken>()), Times.Once);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task ScheduleAsync_WithTypedVariables_SerializesVariablesInsideScheduler() {
        var (scheduler, observer, sp) = Build();
        SchemataJob? captured = null;
        observer.Setup(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()))
                .Callback<SchemataJob, CancellationToken>((j, _) => captured = j)
                .Returns(Task.CompletedTask);

        try {
            await scheduler.StartAsync(CancellationToken.None);

            var job = new SchemataJob {
                Name         = "test/typed-variables",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };

            await scheduler.ScheduleAsync(job, new Dictionary<string, object?> { ["uri"] = "https://rp" },
                                          CancellationToken.None);

            Assert.NotNull(captured);
            var variables = JsonSerializer.Deserialize<Dictionary<string, object?>>(captured!.Variables!);
            Assert.Equal("https://rp", ((JsonElement)variables!["uri"]!).GetString());
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task SkipObserverNotification_WhenScheduleAsyncCalledBeforeStartAsync() {
        var (scheduler, observer, sp) = Build();
        try {
            var job = new SchemataJob {
                Name         = "test/ignored",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };

            await scheduler.ScheduleAsync(job, CancellationToken.None);

            observer.Verify(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()),
                            Times.Never);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task ClearAllEntries_WhenStopAsyncCalled() {
        var (scheduler, observer, sp) = Build();
        try {
            await scheduler.StartAsync(CancellationToken.None);
            var job = new SchemataJob {
                Name         = "test/will-be-cleared",
                JobKey       = typeof(NoopJob).FullName,
                ScheduleType = ScheduleType.OneTime,
                NextRunTime  = DateTime.UtcNow.AddMinutes(5),
                State        = JobState.Active,
            };
            await scheduler.ScheduleAsync(job, CancellationToken.None);

            await scheduler.StopAsync(CancellationToken.None);

            await scheduler.StartAsync(CancellationToken.None);
            await scheduler.ScheduleAsync(job, CancellationToken.None);

            observer.Verify(o => o.OnScheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()),
                            Times.Exactly(2));
            observer.Verify(o => o.OnUnscheduledAsync(It.IsAny<SchemataJob>(), It.IsAny<CancellationToken>()),
                            Times.Never);
        } finally {
            await scheduler.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
    }

    private static IScheduledJobRegistry Registry(params Type[] jobTypes) {
        var registry = new DefaultScheduledJobRegistry();
        registry.RegisterAll(jobTypes);
        return registry;
    }

    #region Nested type: NoopJob

    private sealed class NoopJob : IScheduledJob
    {
        #region IScheduledJob Members

        public Task ExecuteAsync(JobContext context, CancellationToken ct) { return Task.CompletedTask; }

        #endregion
    }

    #endregion
}
