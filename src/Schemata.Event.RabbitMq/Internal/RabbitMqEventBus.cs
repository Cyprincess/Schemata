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

namespace Schemata.Event.RabbitMq.Internal;

/// <summary>RabbitMQ-backed <see cref="IEventBus"/> for cross-process broadcast and request/response.</summary>
public sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private readonly IConnection                    _connection;
    private readonly CorrelationTracker             _correlation;
    private readonly EventOutboxDispatcher?         _dispatcher;
    private readonly JsonSerializerOptions          _json;
    private readonly ILogger<RabbitMqEventBus>?     _logger;
    private readonly IOptions<RabbitMqEventOptions> _options;
    private readonly IEventTypeRegistry             _registry;
    private readonly IChannel                       _replyChannel;
    private readonly string                         _replyQueueName;
    private readonly IServiceProvider               _services;

    /// <summary>Initializes a new <see cref="RabbitMqEventBus" /> and primes the reply queue.</summary>
    public RabbitMqEventBus(
        IOptions<RabbitMqEventOptions> options,
        CorrelationTracker             correlation,
        IEventTypeRegistry             registry,
        IServiceProvider               services,
        IOptions<JsonSerializerOptions> json,
        ILogger<RabbitMqEventBus>?     logger     = null,
        EventOutboxDispatcher?         dispatcher = null
    ) {
        _options     = options;
        _correlation = correlation;
        _registry    = registry;
        _services    = services;
        _json        = json.Value;
        _logger      = logger;
        _dispatcher  = dispatcher;

        var factory = new ConnectionFactory {
            HostName                   = options.Value.HostName,
            Port                       = options.Value.Port,
            UserName                   = options.Value.UserName,
            Password                   = options.Value.Password,
            VirtualHost                = options.Value.VirtualHost,
            RequestedConnectionTimeout = TimeSpan.FromMilliseconds(options.Value.ConnectionTimeoutMs),
        };

        _connection     = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        _replyQueueName = $"reply.{Identifiers.NewUid():n}";

        _replyChannel = _connection.CreateChannelAsync().GetAwaiter().GetResult();
        _replyChannel.QueueDeclareAsync(_replyQueueName, false, true, true).GetAwaiter().GetResult();

        var consumer = new AsyncEventingBasicConsumer(_replyChannel);
        consumer.ReceivedAsync += HandleReplyAsync;

        _replyChannel.BasicConsumeAsync(_replyQueueName, true, consumer).GetAwaiter().GetResult();
    }

    #region IAsyncDisposable Members

    public async ValueTask DisposeAsync() {
        await _replyChannel.CloseAsync();
        _replyChannel.Dispose();
        await _connection.CloseAsync();
        _connection.Dispose();
    }

    #endregion

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

    public async Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse> {
        var exchange           = _options.Value.ExchangeName;
        var routingKey         = _registry.RequireName(typeof(TRequest));
        var responseRoutingKey = _registry.RequireName(typeof(TResponse));

        using var scope = _services.CreateScope();
        var eventCtx = new EventContext(request!, routingKey) {
            Payload = JsonSerializer.Serialize(request, _json),
            CorrelationId = Identifiers.NewUid().ToString("n"),
        };
        var adviceCtx = new AdviceContext(scope.ServiceProvider);

        switch (await Advisor.For<IEventPublishAdvisor>()
                             .RunAsync(adviceCtx, eventCtx, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when adviceCtx.TryGet<TResponse>(out var r) && r is not null:
                eventCtx.Result = r;
                return r;
            case AdviseResult.Block:
            default:
                throw new InvalidOperationException("Request blocked by advisor.");
        }

        var observers = scope.ServiceProvider.GetServices<IEventLifecycleObserver>().ToList();
        foreach (var observer in observers) {
            await observer.OnPublishedAsync(eventCtx, ct);
        }

        await using var channel = await _connection.CreateChannelAsync(new(true, true), ct);

        var body = Encoding.UTF8.GetBytes(eventCtx.Payload ?? string.Empty);

        var tcs                  = new TaskCompletionSource<TResponse>();
        var trackerCorrelationId = _correlation.Track(tcs, TimeSpan.FromMilliseconds(_options.Value.RequestTimeoutMs));

        // BasicProperties.CorrelationId carries the tracker key so HandleReplyAsync can match
        // the reply; EventContext.CorrelationId is the audit key and intentionally differs.
        var props = new BasicProperties {
            ContentType   = "application/json",
            DeliveryMode  = DeliveryModes.Persistent,
            ReplyTo       = _replyQueueName,
            CorrelationId = trackerCorrelationId,
        };

        await channel.ExchangeDeclareAsync(exchange, _options.Value.ExchangeType, true, cancellationToken: ct);
        await channel.QueueBindAsync(_replyQueueName, exchange, responseRoutingKey, cancellationToken: ct);
        await channel.BasicPublishAsync(exchange, routingKey, true, props, body, ct);

        return await tcs.Task.WaitAsync(ct);
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

    private Task HandleReplyAsync(object sender, BasicDeliverEventArgs ea) {
        var correlationId = ea.BasicProperties.CorrelationId;
        if (string.IsNullOrEmpty(correlationId)) {
            return Task.CompletedTask;
        }

        var responseType = _registry.Resolve(ea.RoutingKey);
        if (responseType is null) {
            return Task.CompletedTask;
        }

        var body     = Encoding.UTF8.GetString(ea.Body.Span);
        var response = JsonSerializer.Deserialize(body, responseType, _json);
        _correlation.Complete(correlationId, response);

        return Task.CompletedTask;
    }
}
