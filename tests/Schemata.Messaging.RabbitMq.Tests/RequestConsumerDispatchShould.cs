using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.RabbitMq.Internal;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Internal;
using Xunit;

namespace Schemata.Messaging.RabbitMq.Tests;

/// <summary>
///     Asserts the consumer's generic dispatch point resolves the concrete
///     <see cref="InProcessRequestDispatcher" /> — never the interface <see cref="IRequestDispatcher" />
///     slot, which a configured outbound transport (e.g. RabbitMQ's own dispatcher) could own — and
///     calls <c>SendAsync</c> instead of resolving <see cref="IRequestHandler{TRequest, TResponse}" />
///     directly, so command/query advisors run against the exact same ambient
///     <see cref="AdviceContext" /> instance the handler observes via
///     <see cref="AdviceContext.Current" />. Invokes the consumer host's private generic dispatch
///     method directly (the exact code path <c>HandleAsync</c> calls through reflection), so no
///     broker connection, channel, or delivery is needed.
/// </summary>
public class RequestConsumerDispatchShould
{
    [Fact]
    public async Task RouteAConsumedCommand_ThroughTheScopedDispatcher_SoItsAdvisorRunsAndAmbientContextIsEstablished() {
        var advisor = new RecordingCommandAdvisor();

        AdviceContext? observedByHandler = null;

        var services = new ServiceCollection();
        services.AddScoped<InProcessRequestDispatcher>();
        services.AddSingleton<ICommandAdvisor<Ping>>(advisor);
        services.AddScoped<IRequestHandler<Ping, Unit>>(
            _ => new PingHandler(() => observedByHandler = AdviceContext.Current));

        await using var provider = services.BuildServiceProvider();
        await using var scope    = provider.CreateAsyncScope();

        var method = typeof(RabbitMqRequestConsumerHost)
                    .GetMethod("InvokeHandlerAsync", BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(typeof(Ping), typeof(Unit));

        var task = (Task<object?>)method.Invoke(null, [scope.ServiceProvider, new Ping(), CancellationToken.None])!;
        await task;

        Assert.True(advisor.Invoked);
        Assert.NotNull(advisor.ObservedContext);
        Assert.NotNull(observedByHandler);
        Assert.Same(advisor.ObservedContext, observedByHandler);
    }

    [Fact]
    public async Task RunTheLocalPipeline_WhenOnlyAddRabbitMqRequestDispatcherIsConfigured_AndNoDomainModuleRanFirst() {
        // Standalone configuration: no domain module (AddSchemataFlow, AddSchemataResources, ...)
        // ever ran, so InProcessRequestDispatcher must come from AddRabbitMqRequestDispatcher's own
        // self-registration, not from a module's four-line dispatcher block.
        var advisor = new RecordingCommandAdvisor();

        AdviceContext? observedByHandler = null;

        var services = new ServiceCollection();
        services.AddRabbitMqRequestDispatcher(_ => { });
        services.AddSingleton<ICommandAdvisor<Ping>>(advisor);
        services.AddScoped<IRequestHandler<Ping, Unit>>(
            _ => new PingHandler(() => observedByHandler = AdviceContext.Current));

        await using var provider = services.BuildServiceProvider();
        await using var scope    = provider.CreateAsyncScope();

        var method = typeof(RabbitMqRequestConsumerHost)
                    .GetMethod("InvokeHandlerAsync", BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(typeof(Ping), typeof(Unit));

        var task = (Task<object?>)method.Invoke(null, [scope.ServiceProvider, new Ping(), CancellationToken.None])!;
        await task;

        Assert.True(advisor.Invoked);
        Assert.NotNull(observedByHandler);
    }

    private sealed record Ping : ICommand;

    private sealed class RecordingCommandAdvisor : ICommandAdvisor<Ping>
    {
        public bool Invoked { get; private set; }

        public AdviceContext? ObservedContext { get; private set; }

        public int Order => 0;

        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, Ping a1, CancellationToken ct = default) {
            Invoked         = true;
            ObservedContext = ctx;
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class PingHandler(Action onHandle) : IRequestHandler<Ping, Unit>
    {
        public Task<Unit> HandleAsync(Ping request, CancellationToken ct = default) {
            onHandle();
            return Task.FromResult(Unit.Value);
        }
    }
}
