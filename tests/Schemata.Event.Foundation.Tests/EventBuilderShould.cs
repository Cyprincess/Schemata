using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Foundation.Builders;
using Schemata.Event.Skeleton;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

public sealed class EventBuilderShould
{
    [Fact]
    public void Register_Two_Event_Handlers_Resolves_Both() {
        var services = new ServiceCollection();
        var builder  = new EventBuilder(services);
        builder.UseHandler<TestEvent, FirstHandler>();
        builder.UseHandler<TestEvent, SecondHandler>();

        using var provider = services.BuildServiceProvider();
        using var scope    = provider.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IEventHandler<TestEvent>>();

        Assert.Collection(
            handlers,
            handler => Assert.IsType<FirstHandler>(handler),
            handler => Assert.IsType<SecondHandler>(handler));
    }

    private sealed class TestEvent : IEvent;

    private sealed class FirstHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken ct = default) { return Task.CompletedTask; }
    }

    private sealed class SecondHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken ct = default) { return Task.CompletedTask; }
    }
}
