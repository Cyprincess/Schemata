using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Foundation.Runtime;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Scheduling.Tests.Fixtures;
using Xunit;

namespace Schemata.Scheduling.Tests;

public class SchedulerScopeShould
{
    [Fact]
    public async Task Root_Facade_Captures_Scoped_Propagators_Without_Root_Scope_Resolution() {
        var observed = new List<string>();
        var dispatcher = new Mock<IRequestDispatcher>();
        dispatcher.Setup(d => d.SendAsync<ScheduleJobRequest<SchemataJob>, Unit>(It.IsAny<ScheduleJobRequest<SchemataJob>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Unit.Value);
        dispatcher.Setup(d => d.SendAsync<UnscheduleJobRequest, Unit>(It.IsAny<UnscheduleJobRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Unit.Value);
        dispatcher.Setup(d => d.SendAsync<TriggerJobRequest<SchemataJobExecution>, SchemataJobExecution>(
            It.IsAny<TriggerJobRequest<SchemataJobExecution>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SchemataJobExecution());
        await using var root = new ServiceCollection().AddSingleton(dispatcher.Object).AddSingleton(observed)
            .AddScoped<IMessageContextPropagator, ScopedPropagator>()
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scheduler = new DefaultScheduler<SchemataJob, SchemataJobExecution>(root, Options.Create(new SchemataSchedulingOptions()));
        await scheduler.ScheduleAsync(new(), default);
        await scheduler.ScheduleAsync(new(), null, default);
        await scheduler.UnscheduleAsync("jobs/test", default);
        await scheduler.TriggerAsync<DeclaredProbeJob>(new(), default);
        Assert.Equal(4, observed.FindAll(value => value == "capture-selected").Count);
        Assert.Equal(8, observed.FindAll(value => value == "restore").Count);
    }

    public sealed class ScopedPropagator(List<string> observed) : IMessageContextPropagator
    {
        private bool _restored;
        public void Capture(IDictionary<string, string?> items, IServiceProvider source) {
            observed.Add(_restored ? "capture-selected" : "capture-bootstrap");
            items["custom.scoped"] = "captured";
        }
        public ValueTask RestoreAsync(IReadOnlyDictionary<string, string?> items, IServiceProvider target, CancellationToken ct = default) {
            Assert.Equal("captured", items["custom.scoped"]);
            _restored = true;
            observed.Add("restore");
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Dispose_Async_Only_Dispatcher_After_Every_Facade_Call() {
        var dispatcher = new Mock<IRequestDispatcher>();
        var disposable = dispatcher.As<IAsyncDisposable>();
        var disposed = 0;
        disposable.Setup(d => d.DisposeAsync()).Callback(() => disposed++).Returns(ValueTask.CompletedTask);
        dispatcher.Setup(d => d.SendAsync<ScheduleJobRequest<SchemataJob>, Unit>(It.IsAny<ScheduleJobRequest<SchemataJob>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Unit.Value);
        dispatcher.Setup(d => d.SendAsync<UnscheduleJobRequest, Unit>(It.IsAny<UnscheduleJobRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Unit.Value);
        dispatcher.Setup(d => d.SendAsync<RescheduleJobRequest<SchemataJob>, Unit>(It.IsAny<RescheduleJobRequest<SchemataJob>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Unit.Value);
        dispatcher.Setup(d => d.SendAsync<TriggerJobRequest<SchemataJobExecution>, SchemataJobExecution>(
            It.IsAny<TriggerJobRequest<SchemataJobExecution>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SchemataJobExecution());
        await using var services = new ServiceCollection().AddScoped<IRequestDispatcher>(_ => dispatcher.Object)
            .AddSingleton<IMessageExecutionScopeFactory, MessageExecutionScopeFactory>().BuildServiceProvider();
        var scheduler = new DefaultScheduler<SchemataJob, SchemataJobExecution>(services, Options.Create(new SchemataSchedulingOptions()));
        await scheduler.ScheduleAsync(new(), CancellationToken.None);
        Assert.Equal(1, disposed);
        await scheduler.ScheduleAsync(new(), null, CancellationToken.None);
        Assert.Equal(2, disposed);
        await scheduler.UnscheduleAsync("jobs/test", CancellationToken.None);
        Assert.Equal(3, disposed);
        await scheduler.RescheduleAsync(new(), null, CancellationToken.None);
        Assert.Equal(4, disposed);
        await scheduler.TriggerAsync<DeclaredProbeJob>(new(), CancellationToken.None);
        Assert.Equal(5, disposed);
    }
}
