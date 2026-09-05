using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Schemata.Transport.RabbitMq;

namespace Schemata.Messaging.RabbitMq.Runtime;

/// <summary>Sends requests across the broker and awaits the single reply.</summary>
/// <remarks>
///     Builds on the shared <see cref="IRabbitMqConnectionProvider" /> and
///     <see cref="CorrelationTracker" />; it opens channels but never a connection, so a process
///     still holds exactly one.
/// </remarks>
internal sealed class RabbitMqRequestDispatcher : ICommandDispatcher, IQueryDispatcher, IAsyncDisposable
{
    private readonly IRabbitMqConnectionProvider        _connections;
    private readonly CorrelationTracker                 _correlation;
    private          int                                _disposed;
    private readonly SemaphoreSlim                      _initialization = new(1, 1);
    private readonly JsonSerializerOptions              _json;
    private readonly IOptions<RabbitMqRequestOptions>   _options;
    private readonly string                             _replyQueueName;
    private readonly ConcurrentDictionary<string, Type> _replyTypes = new(StringComparer.Ordinal);
    private readonly IServiceProvider                   _services;
    private          IChannel?                          _replyChannel;

    public RabbitMqRequestDispatcher(
        IOptions<RabbitMqRequestOptions>  options,
        IRabbitMqConnectionProvider       connections,
        CorrelationTracker                correlation,
        IServiceProvider                  services,
        IOptions<JsonSerializerOptions>?  json = null
    ) {
        _options     = options;
        _connections = connections;
        _correlation = correlation;
        _services    = services;
        _json        = json?.Value ?? new JsonSerializerOptions();

        _replyQueueName = $"reply.{Guid.NewGuid():n}";
    }

    #region IAsyncDisposable Members

    public async ValueTask DisposeAsync() {
        // Registered under three interfaces that all resolve to this same scoped instance (see
        // AddRabbitMqRequestDispatcher), so the container's per-service-type disposal tracking
        // calls this more than once; only the first call must run.
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }

        await _initialization.WaitAsync();
        try {
            // Only the reply channel is ours; the connection belongs to the shared provider.
            if (_replyChannel is { } channel) {
                _replyChannel = null;
                await channel.CloseAsync();
                channel.Dispose();
            }
        } finally {
            _initialization.Release();
        }

        _initialization.Dispose();
    }

    #endregion

    #region IRequestDispatcher Members

    public async Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse> {
        var binding = _options.Value.Require(typeof(TRequest));

        // Capture runs here, synchronously, in the CALLER's scope: it is the only place the ambient
        // state exists. Only the flattened dictionary crosses the process boundary — never a scoped
        // object reference.
        var context = MessageContexts.Capture(_services);

        var connection   = await _connections.GetConnectionAsync(ct);
        var replyChannel = await InitializeReplyChannelAsync(ct);
        await using var channel = await connection.CreateChannelAsync(new(true, true), ct);

        var tcs           = new TaskCompletionSource<TResponse>();
        var correlationId = _correlation.Track(tcs, TimeSpan.FromMilliseconds(_options.Value.RequestTimeoutMs));
        _replyTypes[correlationId] = typeof(TResponse);

        var props = new BasicProperties {
            ContentType   = "application/json",
            DeliveryMode  = DeliveryModes.Persistent,
            ReplyTo       = _replyQueueName,
            CorrelationId = correlationId,
            Headers       = MessageContextHeaders.Write(context),
        };

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, _json));

        await channel.ExchangeDeclareAsync(_options.Value.ExchangeName, _options.Value.ExchangeType, true,
                                           cancellationToken: ct);
        await channel.BasicPublishAsync(_options.Value.ExchangeName, binding.Name, true, props, body, ct);

        try {
            return await tcs.Task.WaitAsync(ct);
        } finally {
            _replyTypes.TryRemove(correlationId, out _);

            // The entry's timeout would otherwise fire a TimeoutException at a wrapper nobody
            // observes any more.
            if (_correlation.Abandon(correlationId)) {
                tcs.TrySetCanceled(ct);
            }
        }
    }

    #endregion

    #region ICommandDispatcher Members

    /// <inheritdoc cref="ICommandDispatcher.SendAsync{TCommand}" />
    public Task SendAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand
        => SendAsync<TCommand, Unit>(command, ct);

    #endregion

    private async ValueTask<IChannel> InitializeReplyChannelAsync(CancellationToken ct) {
        if (_replyChannel is { } existing) {
            return existing;
        }

        await _initialization.WaitAsync(ct);
        try {
            if (_replyChannel is { } initialized) {
                return initialized;
            }

            var connection = await _connections.GetConnectionAsync(ct);

            IChannel? channel = null;
            try {
                channel = await connection.CreateChannelAsync(cancellationToken: ct);

                // Exclusive + auto-delete: the reply queue belongs to this dispatcher instance, and
                // replies reach it through the default exchange keyed by the queue name, so no
                // binding and no response-side wire name are needed.
                await channel.QueueDeclareAsync(_replyQueueName, false, true, true, cancellationToken: ct);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += HandleReplyAsync;

                await channel.BasicConsumeAsync(_replyQueueName, true, consumer, ct);
            } catch {
                // Only the half-built channel is discarded; the shared connection stays open for
                // every other client in the process.
                if (channel is not null) {
                    await channel.DisposeAsync();
                }

                throw;
            }

            _replyChannel = channel;
            return channel;
        } finally {
            _initialization.Release();
        }
    }

    private Task HandleReplyAsync(object sender, BasicDeliverEventArgs ea) {
        var correlationId = ea.BasicProperties.CorrelationId;
        if (string.IsNullOrEmpty(correlationId) || !_replyTypes.TryGetValue(correlationId, out var responseType)) {
            return Task.CompletedTask;
        }

        var body = Encoding.UTF8.GetString(ea.Body.Span);

        // The broker boxes header values; a hand-rolled publisher may emit the flag as a string.
        if (ea.BasicProperties.Headers?.TryGetValue(RequestErrorHeaders.RemoteError, out var flagged) == true
         && flagged is true or "true" or "True") {
            var error = JsonSerializer.Deserialize<RemoteRequestError>(body, _json);
            _correlation.Fail(correlationId, new RemoteRequestException(error?.Reason ?? "internal", null));

            return Task.CompletedTask;
        }

        var response = JsonSerializer.Deserialize(body, responseType, _json);
        _correlation.Complete(correlationId, response);

        return Task.CompletedTask;
    }
}
