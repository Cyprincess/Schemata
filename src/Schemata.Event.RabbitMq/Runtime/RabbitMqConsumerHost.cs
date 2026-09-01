using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
using Schemata.Entity.Repository;
using Schemata.Event.Foundation;
using Schemata.Event.Foundation.Runtime;
using Schemata.Event.Foundation.Observers;
using Schemata.Event.Skeleton;
using Schemata.Event.Skeleton.Advisors;
using Schemata.Event.Skeleton.Entities;
using Schemata.Transport.RabbitMq;

namespace Schemata.Event.RabbitMq.Runtime;

/// <summary>Background RabbitMQ consumer that dispatches broker messages into event handlers.</summary>
public sealed class RabbitMqConsumerHost : BackgroundService
{
    private readonly SemaphoreSlim                  _channelLock = new(1, 1);
    private readonly IRabbitMqConnectionProvider    _connections;
    private readonly JsonSerializerOptions          _json;
    private readonly ILogger<RabbitMqConsumerHost>? _logger;
    private readonly IOptions<RabbitMqEventOptions> _options;
    private readonly IServiceProvider               _services;

    /// <summary>Initializes a RabbitMQ consumer host over the configured broker topology.</summary>
    public RabbitMqConsumerHost(
        IServiceProvider               services,
        IRabbitMqConnectionProvider    connections,
        IOptions<RabbitMqEventOptions> options,
        IOptions<JsonSerializerOptions> json,
        ILogger<RabbitMqConsumerHost>? logger = null
    ) {
        _services    = services;
        _connections = connections;
        _options     = options;
        _json        = json.Value;
        _logger      = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct) {
        // The connection is owned by the shared provider and outlives this host; only the channel
        // is ours to dispose.
        var connection = await _connections.GetConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

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

            // Handler failures decide ack vs. nack; broker acknowledgement failures are transport
            // errors outside handler control.
            bool handled;
            try {
                handled = await HandleMessageAsync(channel, ea, ct);
            } catch (Exception ex) {
                _logger?.LogError(ex, "Handler threw for routing key '{RoutingKey}', dead-lettering.", ea.RoutingKey);
                handled = false;
            }

            await _channelLock.WaitAsync(ct);
            try {
                if (handled) {
                    await channel.BasicAckAsync(deliveryTag, false, ct);
                } else {
                    await channel.BasicNackAsync(deliveryTag, false, false, ct);
                }
            } catch (Exception ex) {
                _logger?.LogError(ex, "Failed to acknowledge delivery '{DeliveryTag}' on the broker.", deliveryTag);
            } finally {
                _channelLock.Release();
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

        using var scope         = _services.CreateScope();
        var       subscriptions = scope.ServiceProvider.GetRequiredService<IRepository<SchemataEventSubscription>>();
        var       resolver      = scope.ServiceProvider.GetRequiredService<HandlerResolver>();
        var       registry      = scope.ServiceProvider.GetRequiredService<IEventTypeRegistry>();
        var       tracker       = scope.ServiceProvider.GetService<CorrelationTracker>();

        // Reply correlation comes first because reply payloads bypass subscription matching.
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
        if (eventType is null) {
            _logger?.LogWarning(
                "Received message with unregistered routing key '{RoutingKey}'; routing to dead-letter.",
                eventTypeName);
            return false;
        }

        var matched = new List<SchemataEventSubscription>();
        await foreach (var sub in subscriptions.ListMatchingAsync(eventTypeName, ct: ct)) {
            matched.Add(sub);
        }

        if (matched.Count == 0) {
            // ACK and drop orphan events. The queue is shared with other consumers and orphan
            // events are expected during rolling deploys.
            return true;
        }

        var context = scope.ServiceProvider.GetRequiredService<IEventDispatchContext>();
        context.SetSubscriptions(matched);

        var eventInstance = JsonSerializer.Deserialize(body, eventType, _json);
        if (eventInstance is null) {
            return false;
        }

        var method        = typeof(HandlerResolver).GetMethod(nameof(HandlerResolver.InvokeEventHandlersAsync))!;
        var genericMethod = method.MakeGenericMethod(eventType);

        var routing = registry.GetRouting(eventType);

        var eventForCtx = (IEvent)eventInstance;
        var eventCtx = new EventContext(eventForCtx, eventTypeName) {
            Payload = body,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("n"),
        };

        try {
            object? invoked;
            try {
                invoked = genericMethod.Invoke(resolver, [eventInstance, routing, ct]);
            } catch (TargetInvocationException tie) when (tie.InnerException is not null) {
                // Reflection wraps a synchronous throw from the resolver method; surface the real
                // handler failure.
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }

            if (invoked is Task task) {
                await task;
            }

            eventCtx.Result = true;
        } catch (Exception ex) {
            eventCtx.Exception = ex;
            throw;
        } finally {
            var consumeAdviceCtx = new AdviceContext(scope.ServiceProvider);
            using var _ = AdviceContext.Establish(consumeAdviceCtx);
            switch (await Advisor.For<IEventConsumeAdvisor>()
                                 .RunAsync(consumeAdviceCtx, eventCtx, ct)) {
                case AdviseResult.Continue:
                case AdviseResult.Handle:
                case AdviseResult.Block:
                default:
                    break;
            }

            Exception? observerFailure = null;
            // Audit-last ordering is enforced here regardless of DI registration order: application
            // observers run before SchemataEventAuditObserver so the audit record sees their outcome.
            var observers = scope.ServiceProvider.GetServices<IEventLifecycleObserver>()
                                 .OrderBy(observer => observer is SchemataEventAuditObserver);
            foreach (var observer in observers) {
                try {
                    await observer.OnConsumedAsync(eventCtx, ct);
                } catch (Exception ex) {
                    // The audit observer runs last in the enforced audit-last order, so it persists the
                    // first failure through EventContext.Exception before it escapes to the consumer loop.
                    if (observerFailure is null) {
                        observerFailure    = ex;
                        eventCtx.Exception = ex;
                    }
                }
            }

            if (observerFailure is not null) {
                ExceptionDispatchInfo.Capture(observerFailure).Throw();
            }
        }

        return true;
    }
}
