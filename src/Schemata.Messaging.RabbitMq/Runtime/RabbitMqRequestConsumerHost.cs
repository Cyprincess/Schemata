using System;
using System.Reflection;
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
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Transport.RabbitMq;

namespace Schemata.Messaging.RabbitMq.Runtime;

/// <summary>Serves requests arriving from the broker by invoking the locally registered handler.</summary>
/// <remarks>
///     Runs only when <see cref="RabbitMqRequestOptions.QueueName" /> is set: a process that only
///     sends requests needs no consumer.
/// </remarks>
internal sealed class RabbitMqRequestConsumerHost : BackgroundService
{
    private static readonly MethodInfo InvokeHandler =
        typeof(RabbitMqRequestConsumerHost).GetMethod(nameof(InvokeHandlerAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly IRabbitMqConnectionProvider              _connections;
    private readonly JsonSerializerOptions                    _json;
    private readonly ILogger<RabbitMqRequestConsumerHost>?    _logger;
    private readonly IOptions<RabbitMqRequestOptions>         _options;
    private readonly IServiceScopeFactory                     _scopes;
    private          IChannel?                                _channel;

    public RabbitMqRequestConsumerHost(
        IOptions<RabbitMqRequestOptions>       options,
        IRabbitMqConnectionProvider            connections,
        IServiceScopeFactory                   scopes,
        IOptions<JsonSerializerOptions>?       json   = null,
        ILogger<RabbitMqRequestConsumerHost>?  logger = null
    ) {
        _options     = options;
        _connections = connections;
        _scopes      = scopes;
        _json        = json?.Value ?? new JsonSerializerOptions();
        _logger      = logger;
    }

    public override async Task StopAsync(CancellationToken ct) {
        if (_channel is { } channel) {
            _channel = null;
            await channel.CloseAsync(ct);
            channel.Dispose();
        }

        await base.StopAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken ct) {
        var queue = _options.Value.QueueName;
        if (string.IsNullOrWhiteSpace(queue)) {
            return;
        }

        var connection = await _connections.GetConnectionAsync(ct);
        _channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(_options.Value.ExchangeName, _options.Value.ExchangeType, true,
                                            cancellationToken: ct);
        await _channel.QueueDeclareAsync(queue, true, false, false, cancellationToken: ct);

        foreach (var binding in _options.Value.Bindings.Values) {
            await _channel.QueueBindAsync(queue, _options.Value.ExchangeName, binding.Name, cancellationToken: ct);
        }

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, ea) => HandleAsync(ea, ct);

        await _channel.BasicConsumeAsync(queue, true, consumer, ct);
    }

    private async Task HandleAsync(BasicDeliverEventArgs ea, CancellationToken ct) {
        var binding = _options.Value.Resolve(ea.RoutingKey);
        if (binding is null) {
            _logger?.LogWarning("No request binding registered for routing key {RoutingKey}.", ea.RoutingKey);
            return;
        }

        try {
            // One scope per message. The handler and everything it resolves must come from a scope
            // that carries the caller's context, not from the host's root provider.
            await using var scope = _scopes.CreateAsyncScope();

            var items = MessageContextHeaders.Read(ea.BasicProperties.Headers);
            foreach (var propagator in scope.ServiceProvider.GetServices<IMessageContextPropagator>()) {
                await propagator.RestoreAsync(items, scope.ServiceProvider, ct);
            }

            var body    = Encoding.UTF8.GetString(ea.Body.Span);
            var request = JsonSerializer.Deserialize(body, binding.Request, _json);
            if (request is null) {
                return;
            }

            var invoke = InvokeHandler.MakeGenericMethod(binding.Request, binding.Response);
            var task   = (Task<object?>)invoke.Invoke(null, [scope.ServiceProvider, request, ct])!;
            var result = await task;

            await ReplyAsync(ea, result, ct);
        } catch (Exception ex) {
            // A handler failure must not take the consumer loop down; the caller sees a timeout.
            _logger?.LogError(ex, "Request {RoutingKey} failed.", ea.RoutingKey);
        }
    }

    private static async Task<object?> InvokeHandlerAsync<TRequest, TResponse>(
        IServiceProvider  services,
        object            request,
        CancellationToken ct
    )
        where TRequest : IRequest<TResponse> {
        // The consumer is a pipeline root whose job is LOCAL execution of the request it just
        // received: it must run the in-process advisor + handler pipeline, never the configured
        // outbound IRequestDispatcher. When AddRabbitMqRequestDispatcher is configured, that slot
        // resolves to RabbitMqRequestDispatcher (the outbound sender) via TryAddScoped, which would
        // republish this very delivery back onto the same routing key instead of handling it.
        // InProcessRequestDispatcher is registered by its concrete type — independent of whichever
        // implementation owns the IRequestDispatcher/ICommandDispatcher/IQueryDispatcher slots — by
        // every module capability extension that owns a request handler (AddSchemataFlow,
        // AddSchemataInsight, AddSchemataResources, AddSchemataScheduling), so resolving it directly
        // reaches the local pipeline regardless of what RabbitMq registered for outbound sends.
        var dispatcher = services.GetRequiredService<InProcessRequestDispatcher>();
        return await dispatcher.SendAsync<TRequest, TResponse>((TRequest)request, ct);
    }

    private async Task ReplyAsync(BasicDeliverEventArgs ea, object? result, CancellationToken ct) {
        var replyTo = ea.BasicProperties.ReplyTo;
        if (string.IsNullOrEmpty(replyTo) || _channel is not { } channel) {
            return;
        }

        var props = new BasicProperties {
            ContentType   = "application/json",
            CorrelationId = ea.BasicProperties.CorrelationId,
        };

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, _json));

        // Replies go through the default exchange straight to the caller's exclusive reply queue.
        await channel.BasicPublishAsync(string.Empty, replyTo, true, props, body, ct);
    }
}
