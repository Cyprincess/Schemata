using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Messaging.RabbitMq.Runtime;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Messaging.RabbitMq.Tests;

/// <summary>
///     Asserts the consumer's generic dispatch point resolves the concrete
///     <see cref="InProcessRequestDispatcher" /> — never the interface <see cref="IRequestDispatcher" />
///     slot, which a configured outbound transport (e.g. RabbitMQ's own dispatcher) could own — and
///     calls <c>SendAsync</c> instead of resolving <see cref="IRequestHandler{TRequest, TResponse}" />
///     directly, so command/query advisors and the handler receive the same explicit
///     <see cref="AdviceContext" /> instance. Invokes the consumer host's private generic dispatch
///     method directly (the exact code path <c>HandleAsync</c> calls through reflection), so no
///     broker connection, channel, or delivery is needed.
/// </summary>
public class RequestConsumerDispatchShould
{
    [Fact]
    public async Task RouteAConsumedCommand_ThroughTheScopedDispatcher_SoItsAdvisorAndHandlerShareOneContext() {
        var advisor = new RecordingCommandAdvisor();

        AdviceContext? observedByHandler = null;

        var services = new ServiceCollection();
        services.AddScoped<InProcessRequestDispatcher>();
        services.AddSingleton<IRequestPipelineAdvisor<Ping, Unit>>(advisor);
        services.AddScoped<IRequestHandler<Ping, Unit>>(
            _ => new PingHandler(context => observedByHandler = context));

        await using var provider = services.BuildServiceProvider();
        await using var scope    = provider.CreateAsyncScope();

        var method = typeof(RabbitMqRequestConsumerHost)
                    .GetMethod("InvokeHandlerAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var invoke = method.MakeGenericMethod(typeof(Ping), typeof(Unit));

        var task = Assert.IsAssignableFrom<Task<object?>>(
            invoke.Invoke(null, [scope.ServiceProvider, new Ping(), CancellationToken.None]));
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
        services.AddSingleton<IRequestPipelineAdvisor<Ping, Unit>>(advisor);
        services.AddScoped<IRequestHandler<Ping, Unit>>(
            _ => new PingHandler(context => observedByHandler = context));

        await using var provider = services.BuildServiceProvider();
        await using var scope    = provider.CreateAsyncScope();

        var method = typeof(RabbitMqRequestConsumerHost)
                    .GetMethod("InvokeHandlerAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var invoke = method.MakeGenericMethod(typeof(Ping), typeof(Unit));

        var task = Assert.IsAssignableFrom<Task<object?>>(
            invoke.Invoke(null, [scope.ServiceProvider, new Ping(), CancellationToken.None]));
        await task;

        Assert.True(advisor.Invoked);
        Assert.NotNull(observedByHandler);
        Assert.Same(advisor.ObservedContext, observedByHandler);
    }

    private sealed record Ping : ICommand;

    private sealed class RecordingCommandAdvisor : IRequestPipelineAdvisor<Ping, Unit>
    {
        public bool Invoked { get; private set; }

        public AdviceContext? ObservedContext { get; private set; }

        public int Order => 0;

        public Task<Unit> AdviseAsync(
            AdviceContext                    ctx,
            Ping                             a1,
            RequestHandlerContinuation<Unit> next,
            CancellationToken                ct = default) {
            Invoked         = true;
            ObservedContext = ctx;
            return next(ct);
        }
    }

    private sealed class PingHandler(Action<AdviceContext> onHandle) : IRequestHandler<Ping, Unit>
    {
        public Task<Unit> HandleAsync(Ping request, AdviceContext context, CancellationToken ct = default) {
            onHandle(context);
            return Task.FromResult(Unit.Value);
        }
    }
}
