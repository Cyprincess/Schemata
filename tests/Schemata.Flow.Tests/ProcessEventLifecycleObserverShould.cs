using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Event.Skeleton;
using Schemata.Flow.Event.Events;
using Schemata.Flow.Event.Internal;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessEventLifecycleObserverShould
{
    [Fact]
    public async Task OnTokenCancelledAsync_PublishesTokenCancelledEvent_WithProcessAndTokenCanonicalNames() {
        TokenCancelledEvent? captured = null;

        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(
                It.IsAny<TokenCancelledEvent>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
           .Callback<TokenCancelledEvent, object, CancellationToken>((e, _, _) => captured = e)
           .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(bus.Object);
        var provider = services.BuildServiceProvider();

        var observer = new ProcessEventLifecycleObserver(provider);
        var process  = new SchemataProcess {
            Name          = "p1",
            CanonicalName = "processes/p1",
            Timestamp     = Guid.NewGuid(),
        };
        var token = new TokenSnapshot {
            CanonicalName = "processes/p1/tokens/t1",
            ScopeName     = "p1",
            StateName     = "task-a",
            Status        = "Active",
        };

        await observer.OnTokenCancelledAsync(process, token, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("processes/p1", captured!.ProcessCanonicalName);
        Assert.Equal("processes/p1/tokens/t1", captured.TokenCanonicalName);
        Assert.Equal("task-a", captured.StateName);
    }

    [Fact]
    public async Task OnTokenCancelledAsync_NoEventBus_PublishesNothing() {
        var services = new Mock<IServiceProvider>(MockBehavior.Strict);
        services.Setup(s => s.GetService(typeof(IEventBus))).Returns((object?) null);

        var observer = new ProcessEventLifecycleObserver(services.Object);
        var process  = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var token = new TokenSnapshot {
            CanonicalName = "processes/p1/tokens/t1",
            ScopeName     = "p1",
            StateName     = "task-a",
            Status        = "Active",
        };

        await observer.OnTokenCancelledAsync(process, token, CancellationToken.None);

        services.Verify(s => s.GetService(typeof(IEventBus)), Times.Once);
        services.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnTokenForkedAsync_PublishesTokenForkedEvent_WithSpawnerCanonicalName() {
        TokenForkedEvent? captured = null;
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<TokenForkedEvent>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
           .Callback<TokenForkedEvent, object, CancellationToken>((e, _, _) => captured = e)
           .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(bus.Object);
        var observer = new ProcessEventLifecycleObserver(services.BuildServiceProvider());
        var process  = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var token    = Token("processes/p1/tokens/t2", "task-b");
        var spawner  = Token("processes/p1/tokens/t1", "fork");

        await observer.OnTokenForkedAsync(process, token, spawner, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("processes/p1", captured!.ProcessCanonicalName);
        Assert.Equal("processes/p1/tokens/t2", captured.TokenCanonicalName);
        Assert.Equal("processes/p1/tokens/t1", captured.SpawnerCanonicalName);
        Assert.Equal("task-b", captured.StateName);
    }

    [Fact]
    public async Task OnTokenJoinedAsync_PublishesTokenJoinedEvent_WithInputCanonicalNames() {
        TokenJoinedEvent? captured = null;
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<TokenJoinedEvent>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
           .Callback<TokenJoinedEvent, object, CancellationToken>((e, _, _) => captured = e)
           .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(bus.Object);
        var observer = new ProcessEventLifecycleObserver(services.BuildServiceProvider());
        var process  = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var output   = Token("processes/p1/tokens/out", "after-join");

        await observer.OnTokenJoinedAsync(process, output, [Token("processes/p1/tokens/a", "join"), Token("processes/p1/tokens/b", "join")], CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("processes/p1", captured!.ProcessCanonicalName);
        Assert.Equal("processes/p1/tokens/out", captured.TokenCanonicalName);
        Assert.Equal(["processes/p1/tokens/a", "processes/p1/tokens/b"], captured.InputCanonicalNames);
        Assert.Equal("after-join", captured.StateName);
    }

    private static TokenSnapshot Token(string canonicalName, string stateName) {
        return new() {
            CanonicalName = canonicalName,
            ScopeName     = "p1",
            StateName     = stateName,
            Status        = "Active",
        };
    }
}
