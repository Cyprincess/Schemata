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
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Schemata.Transport.RabbitMq;

namespace Schemata.Event.RabbitMq.Runtime;

/// <summary>
///     RabbitMQ-backed <see cref="IEventBus" /> for cross-process broadcast. Each publish writes a
///     persistent message to the configured exchange and completes once the broker confirms it.
/// </summary>
/// <remarks>
///     Broadcast only. Cross-process request/reply lives in <c>Schemata.Messaging.RabbitMq</c>,
///     which owns its own reply queue and correlation handling.
/// </remarks>
public sealed class RabbitMqEventBus : IEventBus
{
    private readonly IRabbitMqConnectionProvider    _connections;
    private readonly JsonSerializerOptions          _json;
    private readonly ILogger<RabbitMqEventBus>?     _logger;
    private readonly IOptions<RabbitMqEventOptions> _options;
    private readonly IEventTypeRegistry             _registry;
    private readonly IServiceProvider               _services;

    /// <summary>Initializes a new <see cref="RabbitMqEventBus" />.</summary>
    public RabbitMqEventBus(
        IOptions<RabbitMqEventOptions>  options,
        IRabbitMqConnectionProvider     connections,
        IEventTypeRegistry              registry,
        IServiceProvider                services,
        IOptions<JsonSerializerOptions> json,
        ILogger<RabbitMqEventBus>?      logger = null
    ) {
        _options     = options;
        _connections = connections;
        _registry    = registry;
        _services    = services;
        _json        = json.Value;
        _logger      = logger;
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
        var type       = @event.GetType();
        var routingKey = _registry.RequireName(type);

        using var scope = _services.CreateScope();
        var eventCtx = new EventContext(@event, routingKey) {
            Payload       = JsonSerializer.Serialize(@event, type, _json),
            CorrelationId = Guid.NewGuid().ToString("n"),
            Source        = source,
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

        // Publisher confirms hold BasicPublishAsync until the broker accepts the message, so a
        // completed publish is durable.
        var connection = await _connections.GetConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(new(true, true), ct);

        var exchange = _options.Value.ExchangeName;
        var body     = Encoding.UTF8.GetBytes(eventCtx.Payload ?? string.Empty);

        var props = new BasicProperties {
            ContentType   = "application/json",
            DeliveryMode  = DeliveryModes.Persistent,
            CorrelationId = eventCtx.CorrelationId,
        };

        await channel.ExchangeDeclareAsync(exchange, _options.Value.ExchangeType, true, cancellationToken: ct);
        await channel.BasicPublishAsync(exchange, routingKey, true, props, body, ct);

        // The audit observer runs last so the audit row reflects the application observers' outcome.
        foreach (var observer in observers.OrderBy(observer => observer is SchemataEventAuditObserver)) {
            await observer.OnDeliveredAsync(eventCtx, ct);
        }
    }
}
