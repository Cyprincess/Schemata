using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Skeleton;
using Xunit;

namespace Schemata.Event.Foundation.Tests;

/// <summary>
///     Candidate-set semantics of <see cref="HandlerResolver" />: typed handlers and catch-all
///     <see cref="IEventHandler{TEvent}" /> subscribers of <see cref="IEvent" /> form one merged set
///     deduplicated by instance identity, broadcast delivers to every candidate, competing consumers
///     deliver to exactly one, and an empty consume-side set is a configuration error.
/// </summary>
public class HandlerResolverShould
{
    [Trait("Layer", "Component")]
    [Fact]
    public void Resolve_Merges_Typed_Then_CatchAll_Candidates_Each_In_Its_Own_Registration_Order() {
        var typed    = new RecordingTypedHandler();
        var catchAll = new RecordingCatchAllHandler();

        using var provider = new ServiceCollection()
                            .AddSingleton<IEventHandler<SampleEvent>>(typed)
                            .AddSingleton<IEventHandler<IEvent>>(catchAll)
                            .BuildServiceProvider();

        var candidates = new HandlerResolver(provider).ResolveHandlers<SampleEvent>();

        Assert.Equal(2, candidates.Count);
        Assert.Same(typed, candidates[0]);
        Assert.Same(catchAll, candidates[1]);

        // Registration order applies within each contract: typed candidates precede catch-all
        // candidates regardless of which service type was registered first.
        using var reversed = new ServiceCollection()
                            .AddSingleton<IEventHandler<IEvent>>(catchAll)
                            .AddSingleton<IEventHandler<SampleEvent>>(typed)
                            .BuildServiceProvider();

        var reversedCandidates = new HandlerResolver(reversed).ResolveHandlers<SampleEvent>();

        Assert.Same(typed, reversedCandidates[0]);
        Assert.Same(catchAll, reversedCandidates[1]);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Resolve_Deduplicates_The_Same_Instance_Registered_Under_Both_Contracts() {
        var shared = new RecordingDualHandler();

        using var provider = new ServiceCollection()
                            .AddSingleton<IEventHandler<SampleEvent>>(shared)
                            .AddSingleton<IEventHandler<IEvent>>(shared)
                            .BuildServiceProvider();

        var resolver   = new HandlerResolver(provider);
        var candidates = resolver.ResolveHandlers<SampleEvent>();

        Assert.Single(candidates);

        await resolver.InvokeEventHandlersAsync(new SampleEvent(), EventRouting.Broadcast, CancellationToken.None);

        Assert.Equal(1, shared.TypedCalls);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Resolve_Keeps_Distinct_Instances_Of_The_Same_Implementation_Type() {
        var typed    = new RecordingDualHandler();
        var catchAll = new RecordingDualHandler();

        using var provider = new ServiceCollection()
                            .AddSingleton<IEventHandler<SampleEvent>>(typed)
                            .AddSingleton<IEventHandler<IEvent>>(catchAll)
                            .BuildServiceProvider();

        var resolver = new HandlerResolver(provider);

        Assert.Equal(2, resolver.ResolveHandlers<SampleEvent>().Count);

        await resolver.InvokeEventHandlersAsync(new SampleEvent(), EventRouting.Broadcast, CancellationToken.None);

        Assert.Equal(1, typed.TypedCalls);
        Assert.Equal(1, catchAll.TypedCalls);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public void Resolve_For_IEvent_Itself_Resolves_The_CatchAll_Collection_Once() {
        var first  = new RecordingCatchAllHandler();
        var second = new RecordingCatchAllHandler();

        using var provider = new ServiceCollection()
                            .AddSingleton<IEventHandler<IEvent>>(first)
                            .AddSingleton<IEventHandler<IEvent>>(second)
                            .BuildServiceProvider();

        var candidates = new HandlerResolver(provider).ResolveHandlers<IEvent>();

        Assert.Equal(2, candidates.Count);
        Assert.Same(first, candidates[0]);
        Assert.Same(second, candidates[1]);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Invoke_With_Broadcast_Delivers_To_Every_Candidate() {
        var typed    = new RecordingTypedHandler();
        var catchAll = new RecordingCatchAllHandler();

        using var provider = new ServiceCollection()
                            .AddSingleton<IEventHandler<SampleEvent>>(typed)
                            .AddSingleton<IEventHandler<IEvent>>(catchAll)
                            .BuildServiceProvider();

        await new HandlerResolver(provider)
             .InvokeEventHandlersAsync(new SampleEvent(), EventRouting.Broadcast, CancellationToken.None);

        Assert.Equal(1, typed.Calls);
        Assert.Equal(1, catchAll.Calls);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Invoke_With_CompetingConsumers_Delivers_To_Exactly_One_Candidate() {
        var first    = new RecordingTypedHandler();
        var second   = new RecordingTypedHandler();
        var catchAll = new RecordingCatchAllHandler();

        using var provider = new ServiceCollection()
                            .AddSingleton<IEventHandler<SampleEvent>>(first)
                            .AddSingleton<IEventHandler<SampleEvent>>(second)
                            .AddSingleton<IEventHandler<IEvent>>(catchAll)
                            .BuildServiceProvider();

        await new HandlerResolver(provider)
             .InvokeEventHandlersAsync(new SampleEvent(), EventRouting.CompetingConsumers, CancellationToken.None);

        Assert.Equal(1, first.Calls + second.Calls + catchAll.Calls);
    }

    [Trait("Layer", "Component")]
    [Fact]
    public async Task Invoke_Without_Candidates_Throws_The_Consume_Side_Configuration_Error() {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new HandlerResolver(provider).InvokeEventHandlersAsync(
                new SampleEvent(), EventRouting.Broadcast, CancellationToken.None));

        Assert.Contains(nameof(SampleEvent), ex.Message);
    }

    public sealed class SampleEvent : IEvent;

    public sealed class RecordingTypedHandler : IEventHandler<SampleEvent>
    {
        public int Calls;

        public Task HandleAsync(SampleEvent @event, CancellationToken ct = default) {
            Calls++;
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingCatchAllHandler : IEventHandler<IEvent>
    {
        public int Calls;

        public Task HandleAsync(IEvent @event, CancellationToken ct = default) {
            Calls++;
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingDualHandler : IEventHandler<SampleEvent>, IEventHandler<IEvent>
    {
        public int TypedCalls;

        public Task HandleAsync(SampleEvent @event, CancellationToken ct = default) {
            TypedCalls++;
            return Task.CompletedTask;
        }

        public Task HandleAsync(IEvent @event, CancellationToken ct = default) {
            return HandleAsync((SampleEvent)@event, ct);
        }
    }
}
