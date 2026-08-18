using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Schemata.Common;
using Schemata.Event.Skeleton;
using Schemata.Transport.RabbitMq;

namespace Schemata.Event.RabbitMq.Internal;

/// <summary>
///     Replays a persisted event to RabbitMQ for the outbox dispatcher, using a
///     publisher-confirm channel so a confirmed publish is durable and reuses the existing audit row.
/// </summary>
public sealed class RabbitMqEventOutboxPublisher : IEventOutboxPublisher
{
    private readonly IRabbitMqConnectionProvider            _connections;
    private readonly JsonSerializerOptions                  _json;
    private readonly ILogger<RabbitMqEventOutboxPublisher>? _logger;
    private readonly IOptions<RabbitMqEventOptions>         _options;
    private readonly IEventTypeRegistry                     _registry;
    private readonly IServiceProvider                       _services;

    /// <summary>Initializes an outbox publisher over the shared RabbitMQ connection.</summary>
    public RabbitMqEventOutboxPublisher(
        IOptions<RabbitMqEventOptions>         options,
        IRabbitMqConnectionProvider            connections,
        IServiceProvider                       services,
        IEventTypeRegistry                     registry,
        IOptions<JsonSerializerOptions>        json,
        ILogger<RabbitMqEventOutboxPublisher>? logger = null
    ) {
        _options     = options;
        _connections = connections;
        _services    = services;
        _registry    = registry;
        _json        = json.Value;
        _logger      = logger;
    }

    #region IEventOutboxPublisher Members

    public async Task<EventOutboxDelivery> PublishAsync(EventOutboxMessage message, CancellationToken ct = default) {
        // Publisher confirms make BasicPublishAsync complete only when the broker accepts the
        // message, so the dispatcher marks the row delivered only after a durable publish.
        var connection = await _connections.GetConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(new(true, true), ct);

        var exchange = _options.Value.ExchangeName;
        var body     = Encoding.UTF8.GetBytes(message.Payload ?? string.Empty);

        var props = new BasicProperties {
            ContentType   = "application/json",
            DeliveryMode  = DeliveryModes.Persistent,
            CorrelationId = message.CorrelationId,
        };

        await channel.ExchangeDeclareAsync(exchange, _options.Value.ExchangeType, true, cancellationToken: ct);
        await channel.BasicPublishAsync(exchange, message.EventType, true, props, body, ct);

        await NotifyDeliveredAsync(message, ct);

        // The broker accepted the message; a downstream consumer records the terminal state later.
        return EventOutboxDelivery.Delivered;
    }

    #endregion

    private async Task NotifyDeliveredAsync(EventOutboxMessage message, CancellationToken ct) {
        var eventType = _registry.Resolve(message.EventType);
        if (eventType is null) {
            _logger?.LogWarning(
                "Event type '{EventType}' is not registered; skipping OnDeliveredAsync notification.",
                message.EventType);
            return;
        }

        IEvent @event;
        try {
            if (JsonSerializer.Deserialize(message.Payload ?? string.Empty, eventType, _json) is not IEvent parsedEvent) {
                return;
            }

            @event = parsedEvent;
        } catch (Exception ex) {
            _logger?.LogWarning(ex,
                                "Could not deserialize payload for '{EventType}'; skipping OnDeliveredAsync notification.",
                                message.EventType);
            return;
        }

        var eventCtx = new EventContext(@event, message.EventType) {
            Payload       = message.Payload,
            CorrelationId = message.CorrelationId ?? Identifiers.NewUid().ToString("n"),
        };

        using var scope     = _services.CreateScope();
        var       observers = scope.ServiceProvider.GetServices<IEventLifecycleObserver>();
        foreach (var observer in observers) {
            try {
                await observer.OnDeliveredAsync(eventCtx, ct);
            } catch (Exception ex) {
                _logger?.LogWarning(ex,
                                    "IEventLifecycleObserver.OnDeliveredAsync threw for event '{EventType}'.",
                                    message.EventType);
            }
        }
    }
}
