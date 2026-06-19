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

namespace Schemata.Event.RabbitMq.Internal;

/// <summary>
///     Replays a persisted event to RabbitMQ for the outbox dispatcher, using a
///     publisher-confirm channel so a confirmed publish is durable and reuses the existing audit row.
/// </summary>
public sealed class RabbitMqEventOutboxPublisher : IEventOutboxPublisher, IAsyncDisposable
{
    private readonly IConnection                            _connection;
    private readonly JsonSerializerOptions                  _json;
    private readonly ILogger<RabbitMqEventOutboxPublisher>? _logger;
    private readonly IOptions<RabbitMqEventOptions>         _options;
    private readonly IEventTypeRegistry                     _registry;
    private readonly IServiceProvider                       _services;

    /// <summary>Initializes an outbox publisher over the configured RabbitMQ connection.</summary>
    public RabbitMqEventOutboxPublisher(
        IOptions<RabbitMqEventOptions>         options,
        IServiceProvider                       services,
        IEventTypeRegistry                     registry,
        IOptions<JsonSerializerOptions>        json,
        ILogger<RabbitMqEventOutboxPublisher>? logger = null
    ) {
        _options  = options;
        _services = services;
        _registry = registry;
        _json     = json.Value;
        _logger   = logger;

        var factory = new ConnectionFactory {
            HostName                   = options.Value.HostName,
            Port                       = options.Value.Port,
            UserName                   = options.Value.UserName,
            Password                   = options.Value.Password,
            VirtualHost                = options.Value.VirtualHost,
            RequestedConnectionTimeout = TimeSpan.FromMilliseconds(options.Value.ConnectionTimeoutMs),
        };

        _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
    }

    #region IAsyncDisposable Members

    public ValueTask DisposeAsync() {
        return _connection.DisposeAsync();
    }

    #endregion

    #region IEventOutboxPublisher Members

    public async Task<EventOutboxDelivery> PublishAsync(EventOutboxMessage message, CancellationToken ct = default) {
        // Publisher confirms make BasicPublishAsync complete only when the broker accepts the
        // message, so the dispatcher marks the row delivered only after a durable publish.
        await using var channel = await _connection.CreateChannelAsync(new(true, true), ct);

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

        IEvent? @event;
        try {
            @event = JsonSerializer.Deserialize(message.Payload ?? string.Empty, eventType, _json) as IEvent;
        } catch (Exception ex) {
            _logger?.LogWarning(ex,
                                "Could not deserialize payload for '{EventType}'; skipping OnDeliveredAsync notification.",
                                message.EventType);
            return;
        }

        if (@event is null) {
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
