using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Event.Foundation;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Schemata.Transport.RabbitMq;

namespace Schemata.Event.RabbitMq.Internal;

/// <summary>RabbitMQ-backed <see cref="IEventBus"/> for cross-process broadcast.</summary>
/// <remarks>
///     Broadcast only. Cross-process request/reply lives in <c>Schemata.Messaging.RabbitMq</c>,
///     which owns its own reply queue and correlation handling.
/// </remarks>
public sealed class RabbitMqEventBus : IEventBus
{
    private readonly IRabbitMqConnectionProvider    _connections;
    private readonly EventOutboxDispatcher?         _dispatcher;
    private readonly JsonSerializerOptions          _json;
    private readonly ILogger<RabbitMqEventBus>?     _logger;
    private readonly IOptions<RabbitMqEventOptions> _options;
    private readonly IEventTypeRegistry             _registry;
    private readonly IServiceProvider               _services;

    /// <summary>Initializes a new <see cref="RabbitMqEventBus" />.</summary>
    public RabbitMqEventBus(
        IOptions<RabbitMqEventOptions> options,
        IRabbitMqConnectionProvider    connections,
        IEventTypeRegistry             registry,
        IServiceProvider               services,
        IOptions<JsonSerializerOptions> json,
        ILogger<RabbitMqEventBus>?     logger     = null,
        EventOutboxDispatcher?         dispatcher = null
    ) {
        _options     = options;
        _connections = connections;
        _registry    = registry;
        _services    = services;
        _json        = json.Value;
        _logger      = logger;
        _dispatcher  = dispatcher;
    }

    #region IEventBus Members

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent {
        return PublishCoreAsync(@event, null, ct);
    }

    public async Task PublishAsync<TEvent>(TEvent @event, object sourceEntity, CancellationToken ct = default)
        where TEvent : IEvent {
        EventSourceContract.Ensure(sourceEntity);
        await PublishCoreAsync(@event, sourceEntity, ct);
    }

    #endregion

    private async Task PublishCoreAsync<TEvent>(TEvent @event, object? source, CancellationToken ct)
        where TEvent : IEvent {
        // Resolve by the runtime type so a derived event published through a base/interface
        // static type keeps its registered name and serialized derived members.
        var type       = @event!.GetType();
        var routingKey = _registry.RequireName(type);

        using var scope = _services.CreateScope();
        var eventCtx = new EventContext(@event, routingKey) {
            Payload                = JsonSerializer.Serialize(@event, type, _json),
            CorrelationId          = Identifiers.NewUid().ToString("n"),
            RequiresOutboxDelivery = true,
            Source                 = source,
        };
        var adviceCtx = new AdviceContext(scope.ServiceProvider);
        using var _ = AdviceContext.Establish(adviceCtx);

        switch (await Advisor.For<IEventPublishAdvisor>()
                             .RunAsync(adviceCtx, eventCtx, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when adviceCtx.TryGet<object>(out var r):
                eventCtx.Result = r;
                return;
            case AdviseResult.Block:
            default:
                throw new InvalidOperationException("Event publish blocked by advisor.");
        }

        var observers = scope.ServiceProvider.GetServices<IEventLifecycleObserver>().ToList();
        foreach (var observer in observers) {
            await observer.OnPublishedAsync(eventCtx, ct);
        }
        _dispatcher?.NotifyPending();
    }
}
