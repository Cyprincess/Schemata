using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Event.Foundation;
using Schemata.Event.Foundation.Internal;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;

namespace Schemata.Event.RabbitMq.Internal;

public sealed class RabbitMqConsumerHost : BackgroundService
{
    private readonly SemaphoreSlim                  _channelLock = new(1, 1);
    private readonly JsonSerializerOptions          _json;
    private readonly ILogger<RabbitMqConsumerHost>? _logger;
    private readonly IOptions<RabbitMqEventOptions> _options;
    private readonly IServiceProvider               _services;

    public RabbitMqConsumerHost(
        IServiceProvider               services,
        IOptions<RabbitMqEventOptions> options,
        JsonSerializerOptions?         json   = null,
        ILogger<RabbitMqConsumerHost>? logger = null
    ) {
        _services = services;
        _options  = options;
        _json     = json ?? new JsonSerializerOptions();
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct) {
        var factory = new ConnectionFactory {
            HostName    = _options.Value.HostName,
            Port        = _options.Value.Port,
            UserName    = _options.Value.UserName,
            Password    = _options.Value.Password,
            VirtualHost = _options.Value.VirtualHost,
        };

        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel    = await connection.CreateChannelAsync(cancellationToken: ct);

        var exchange = _options.Value.ExchangeName;
        var queue    = _options.Value.QueueName;
        var dlx      = _options.Value.DeadLetterExchange;

        await channel.ExchangeDeclareAsync(exchange, _options.Value.ExchangeType, true, cancellationToken: ct);

        // Declare the dead-letter exchange and bind the queue's x-dead-letter-exchange so the
        // broker fans out rejected messages (handler throw, unknown type, deserialization error)
        // into a topology operators can drain or replay from.
        var args = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(dlx)) {
            await channel.ExchangeDeclareAsync(dlx, "topic", true, cancellationToken: ct);
            args["x-dead-letter-exchange"] = dlx;
            if (!string.IsNullOrWhiteSpace(_options.Value.DeadLetterRoutingKey)) {
                args["x-dead-letter-routing-key"] = _options.Value.DeadLetterRoutingKey;
            }
        }

        await channel.QueueDeclareAsync(queue, true, false, false, args, cancellationToken: ct);
        await channel.QueueBindAsync(queue, exchange, "#", cancellationToken: ct);

        // Bounded prefetch: the broker stops sending new messages once the unacknowledged
        // window reaches PrefetchCount, providing per-consumer backpressure.
        await channel.BasicQosAsync(0, _options.Value.PrefetchCount, false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) => {
            var deliveryTag = ea.DeliveryTag;
            try {
                var handled = await HandleMessageAsync(channel, ea, ct);
                await _channelLock.WaitAsync(ct);
                try {
                    if (handled) {
                        await channel.BasicAckAsync(deliveryTag, false, ct);
                    } else {
                        await channel.BasicNackAsync(deliveryTag, false, false, ct);
                    }
                } finally {
                    _channelLock.Release();
                }
            } catch (Exception ex) {
                _logger?.LogError(ex, "Handler threw for routing key '{RoutingKey}', dead-lettering.", ea.RoutingKey);
                await _channelLock.WaitAsync(ct);
                try {
                    await channel.BasicNackAsync(deliveryTag, false, false, ct);
                } finally {
                    _channelLock.Release();
                }
            }
        };

        await channel.BasicConsumeAsync(queue, false, consumer, ct);

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task<bool> HandleMessageAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct) {
        var eventTypeName = ea.RoutingKey;
        var correlationId = ea.BasicProperties.CorrelationId;
        var replyTo       = ea.BasicProperties.ReplyTo;
        var body          = Encoding.UTF8.GetString(ea.Body.Span);

        using var scope    = _services.CreateScope();
        var       store    = scope.ServiceProvider.GetRequiredService<IEventSubscriptionStore>();
        var       resolver = scope.ServiceProvider.GetRequiredService<HandlerResolver>();
        var       registry = scope.ServiceProvider.GetRequiredService<IEventTypeRegistry>();
        var       tracker  = scope.ServiceProvider.GetService<CorrelationTracker>();

        // Reply correlation comes first because reply payloads do not need a subscription.
        if (!string.IsNullOrEmpty(correlationId) && tracker != null) {
            var responseType = registry.Resolve(eventTypeName);
            if (responseType != null) {
                var response = JsonSerializer.Deserialize(body, responseType, _json);
                if (tracker.Complete(correlationId, response)) {
                    return true;
                }
            }
        }

        // Routing-key -> registered Type. Unregistered names are poison messages.
        var eventType = registry.Resolve(eventTypeName);
        if (eventType == null) {
            _logger?.LogWarning(
                "Received message with unregistered routing key '{RoutingKey}'; routing to dead-letter.",
                eventTypeName);
            return false;
        }

        if (!string.IsNullOrEmpty(replyTo)) {
            return await HandleRequestAsync(channel, resolver, registry, eventType, body, correlationId, ct);
        }

        var subscriptions = await store.FindAsync(eventTypeName, ct: ct);
        if (subscriptions.Count == 0) {
            // No interested subscribers - ACK and drop. The queue is shared with other
            // consumers and orphan events are expected during rolling deploys.
            return true;
        }

        var context = scope.ServiceProvider.GetRequiredService<IEventDispatchContext>();
        context.SetSubscriptions(subscriptions);

        var eventInstance = JsonSerializer.Deserialize(body, eventType, _json);
        if (eventInstance == null) {
            return false;
        }

        var method        = typeof(HandlerResolver).GetMethod(nameof(HandlerResolver.InvokeEventHandlersAsync))!;
        var genericMethod = method.MakeGenericMethod(eventType);

        var routing = scope.ServiceProvider.GetRequiredService<IOptions<SchemataEventOptions>>()
                           .Value.RoutingTable.GetValueOrDefault(eventType, EventRouting.Broadcast);

        var eventForCtx = (IEvent)eventInstance;
        var eventCtx = new EventContext(eventForCtx, eventTypeName) {
            Payload = body,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("n"),
        };

        try {
            if (genericMethod.Invoke(resolver, [eventInstance, routing, ct]) is Task task) {
                await task;
            }

            eventCtx.Result = true;
        } catch (Exception ex) {
            eventCtx.Exception = ex;
            throw;
        } finally {
            var consumeAdviceCtx = new AdviceContext(scope.ServiceProvider);
            switch (await Advisor.For<IEventConsumeAdvisor>()
                                 .RunAsync(consumeAdviceCtx, eventCtx, ct)) {
                case AdviseResult.Continue:
                case AdviseResult.Handle:
                case AdviseResult.Block:
                default:
                    break;
            }

            foreach (var observer in scope.ServiceProvider.GetServices<IEventLifecycleObserver>()) {
                try {
                    await observer.OnConsumedAsync(eventCtx, ct);
                } catch (Exception ex) {
                    _logger?.LogWarning(ex,
                                        "IEventLifecycleObserver.OnConsumedAsync threw for event '{EventType}'.",
                                        eventCtx.EventType);
                }
            }
        }

        return true;
    }

    private async Task<bool> HandleRequestAsync(
        IChannel           channel,
        HandlerResolver    resolver,
        IEventTypeRegistry registry,
        Type               requestType,
        string             body,
        string?            correlationId,
        CancellationToken  ct
    ) {
        var responseType = GetResponseType(requestType);
        if (responseType == null) {
            return false;
        }

        var request = JsonSerializer.Deserialize(body, requestType, _json);
        if (request == null) {
            return false;
        }

        var method        = typeof(HandlerResolver).GetMethod(nameof(HandlerResolver.InvokeRequestHandlerAsync))!;
        var genericMethod = method.MakeGenericMethod(requestType, responseType);
        var result        = genericMethod.Invoke(resolver, [request, ct]);
        if (result is not Task task) {
            return false;
        }

        await task;

        var response           = task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task);
        var responseRoutingKey = registry.RequireName(responseType);
        var responseBody       = JsonSerializer.SerializeToUtf8Bytes(response, responseType, _json);

        var props = new BasicProperties {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            CorrelationId = correlationId,
        };

        await _channelLock.WaitAsync(ct);
        try {
            await channel.BasicPublishAsync(_options.Value.ExchangeName, responseRoutingKey, true, props, responseBody, ct);
        } finally {
            _channelLock.Release();
        }

        return true;
    }

    private static Type? GetResponseType(Type requestType) {
        foreach (var type in requestType.GetInterfaces()) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequest<>)) {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
