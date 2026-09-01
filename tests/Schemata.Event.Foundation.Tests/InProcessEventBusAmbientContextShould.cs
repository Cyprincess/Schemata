using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

/// <summary>
///     Asserts <see cref="InProcessEventBus" /> establishes the <see cref="AdviceContext" /> it
///     creates for <see cref="IEventPublishAdvisor" /> as the ambient context for the duration of
///     the publish call, so a publish advisor (and anything it calls) observes the same instance
///     via <see cref="AdviceContext.Current" /> that the pipeline is running.
/// </summary>
public class InProcessEventBusAmbientContextShould
{
    [Fact]
    public async Task Establish_TheDispatchContext_AsAmbient_ForThePublishAdvisor() {
        var registry = new Mock<IEventTypeRegistry>();
        registry.Setup(r => r.RequireName(typeof(SampleEvent))).Returns("sample");
        var advisor  = new RecordingPublishAdvisor();

        await using var services = new ServiceCollection()
                                   .AddSingleton(registry.Object)
                                   .AddSingleton<IEventPublishAdvisor>(advisor)
                                   .BuildServiceProvider();

        var bus = new InProcessEventBus(services, Options.Create(new JsonSerializerOptions()));

        await bus.PublishAsync(new SampleEvent());

        Assert.True(advisor.ObservedAmbientContext);
        Assert.Null(AdviceContext.Current);
    }

    private sealed class SampleEvent : IEvent;

    private sealed class RecordingPublishAdvisor : IEventPublishAdvisor
    {
        public bool ObservedAmbientContext { get; private set; }

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, EventContext a1, CancellationToken ct = default) {
            ObservedAmbientContext = ReferenceEquals(ctx, AdviceContext.Current);
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
